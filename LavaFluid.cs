using System;
using System.Collections.Generic;

namespace Immersion {
// A reduced 2D two-phase model for the small viewer strip, not a calibrated CFD solver.
// Wax: Clavet et al. (2005), double-density relaxation and pairwise viscosity.
// Carrier liquid: semi-Lagrangian advection + pressure projection (Stam, 1999).
// Heating/cooling changes wax buoyancy; no particle follows a prescribed path.
// Upright-lamp coordinates rotated into the strip: +X is upward, -X is gravity,
// the left end is the heated base and the right end is the cool top. Y is transverse.
public sealed class LavaFluid {
    public sealed class Particle {
        public double X,Y,VX,VY,Heat,Color,OldX,OldY;
    }
    public readonly Particle[] Particles;
    public readonly double Width;
    public const double Support=0.26;
    // Tuning. Wax is thick and cohesive, rises slowly and follows the carrier liquid only loosely.
    const double WaxViscosity=4.5;      // cooler wax is thicker; see the pairwise heat factor
    const double WaxConduction=0.22;    // slow conduction preserves warm cores and cooler surfaces
    const double Cohesion=0.55;         // extra pull on under-dense (surface) wax: round drops, necks that pinch
    const double Buoyancy=0.65;         // density contrast opposed by viscous liquid drag
    const double WaxDrag=2.2;
    const double SideFollow=0.85;
    const double CarrierBuoyancy=0.18;   // broad thermal return currents, without synthetic stirring
    const double HeaterWidth=16;        // heater profile across the narrow base, perpendicular to rise
    const double MaxSideSpeed=0.16,MaxRiseSpeed=0.075;
    const int MaxParticles=1500;
    readonly int nx,ny=20,hashWidth,hashHeight;
    readonly double dx,dy,phase,heatRate,coolRate;
    readonly double[] u,v,oldU,oldV,temperature,oldTemperature,dye,oldDye,pressure,divergence,profile;
    readonly List<int>[] buckets;
    readonly int[] neighbors;
    readonly double[] distances;
    readonly double[] heaters,power;
    double elapsed;
    public double Elapsed {get{return elapsed;}}
    static double Clamp(double n,double lo,double hi) {return Math.Max(lo,Math.Min(hi,n));}
    public LavaFluid(double aspect,int seed) {
        // Slightly deeper simulation space makes convection legible in a shallow strip.
        Width=Clamp(aspect*0.70,3,32);
        var random=new Random(seed);phase=random.NextDouble()*Math.PI*2;
        heatRate=0.045+random.NextDouble()*0.015;coolRate=0.035+random.NextDouble()*0.012;
        nx=Math.Max(48,Math.Min(192,(int)(Width*10)));dx=Width/(nx-1);dy=1.0/(ny-1);
        int size=nx*ny;
        u=new double[size];v=new double[size];oldU=new double[size];oldV=new double[size];
        temperature=new double[size];oldTemperature=new double[size];dye=new double[size];oldDye=new double[size];
        pressure=new double[size];divergence=new double[size];
        // The heater lies at the left end, corresponding to the base of an upright lamp.
        int cells=2;heaters=new double[cells];power=new double[cells];
        for(int i=0;i<cells;i++) {
            // Fixed, steady heat: variation is spatial, never random per frame.
            heaters[i]=(i+0.3+random.NextDouble()*0.4)/cells;
            power[i]=0.78+random.NextDouble()*0.22;
        }
        profile=new double[nx];double meanProfile=0;
        UpdateHeaters();
        for(int x=0;x<nx;x++)meanProfile+=profile[x];
        meanProfile/=nx;
        // Rotated streamfunction: axial ascent and transverse return flow. Its curl
        // creates a closed circulation, with no normal velocity at either end.
        var stream=new double[nx];double running=0,streamMean=0;
        for(int x=0;x<nx;x++){running+=(profile[x]-meanProfile)/(nx-1);stream[x]=running;streamMean+=running;}
        streamMean/=nx;
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++) {
            int at=y*nx+x;double px=x*dx,py=y*dy;
            int transverse=(int)(py*(nx-1));double s=stream[transverse]-streamMean;
            u[at]=0.04*(profile[transverse]-meanProfile)*Math.Pow(Math.Sin(Math.PI*px/Width),2);
            v[at]=-0.04*s*Math.PI/Width*Math.Sin(2*Math.PI*px/Width);
            temperature[at]=Clamp(0.9-0.8*px/Width+0.05*profile[transverse]*(1-px/Width),0,1);
            dye[at]=2.5+1.3*Math.Sin(px*0.7+phase)+0.8*Math.Cos(px*1.7-py*3+phase);
        }
        Boundaries();
        // Mixed-size wax bodies with overlapping lobes and satellite drops.
        // Some rest in the warm reservoir; the rest start at different thermal phases.
        int groups=Math.Max(3,(int)Math.Round(Width/(2.9+random.NextDouble()*0.6)));
        var sizes=new int[groups];int blobTotal=0;
        for(int i=0;i<groups;i++){sizes[i]=26+(int)(Math.Pow(random.NextDouble(),1.5)*100);blobTotal+=sizes[i];}
        int satelliteCount=Math.Max(1,(int)Math.Round(Width/10.0));
        var satelliteSizes=new int[satelliteCount];int satelliteTotal=0;
        for(int i=0;i<satelliteCount;i++){satelliteSizes[i]=10+random.Next(16);satelliteTotal+=satelliteSizes[i];}
        // Leave clear carrier liquid between drops and bound the cost of wide strips.
        int cap=Math.Min(MaxParticles,Math.Max(180,(int)(Width*28)));
        if(blobTotal+satelliteTotal>cap) {
            double scale=(double)Math.Max(60,cap-satelliteTotal)/blobTotal;blobTotal=0;
            for(int i=0;i<groups;i++){sizes[i]=Math.Max(20,(int)(sizes[i]*scale));blobTotal+=sizes[i];}
        }
        int count=blobTotal+satelliteTotal;
        Particles=new Particle[count];neighbors=new int[count];distances=new double[count];
        int index=0;
        for(int group=0;group<groups;group++) {
            int members=sizes[group];
            bool resting=group==0;
            double centerX=(group+0.25+random.NextDouble()*0.5)*Width/groups;
            int lobes=members<45?1:(members<85?2:3);
            var share=new double[lobes];double shareTotal=0;
            for(int l=0;l<lobes;l++){share[l]=0.55+random.NextDouble();shareTotal+=share[l];}
            var lobeMembers=new int[lobes];int assigned=0;
            for(int l=1;l<lobes;l++){lobeMembers[l]=Math.Max(10,(int)Math.Round(members*share[l]/shareTotal));assigned+=lobeMembers[l];}
            lobeMembers[0]=Math.Max(10,members-assigned);
            members=0;for(int l=0;l<lobes;l++)members+=lobeMembers[l];
            // Lobe shapes: wide, flat ellipses for the pool, rounder ones for floating blobs; height is capped
            // because the glass is shallow.
            var lobeA=new double[lobes];var lobeB=new double[lobes];var lobeX=new double[lobes];
            for(int l=0;l<lobes;l++) {
                double r=Math.Sqrt(lobeMembers[l])*0.045,stretch=resting?1.2+random.NextDouble()*0.5:0.85+random.NextDouble()*0.6;
                double b=Math.Min(resting?0.19:0.29,r/stretch);lobeB[l]=b;lobeA[l]=r*r/b;
            }
            for(int l=1;l<lobes;l++)lobeX[l]=lobeX[l-1]+0.7*(lobeA[l-1]+lobeA[l]);
            bool cold=!resting&&group%2==1;
            double mid=lobeX[lobes-1]/2,baseY=0.35+random.NextDouble()*0.3;
            double heat=resting?0.85+random.NextDouble()*0.15:(cold?0.08+random.NextDouble()*0.15:0.75+random.NextDouble()*0.2);
            double color=random.Next(6);
            double span=lobeX[lobes-1]+lobeA[0]+lobeA[lobes-1];
            centerX=Clamp(centerX,span/2+0.05,Math.Max(span/2+0.05,Width-span/2-0.05));
            for(int l=0;l<lobes;l++) {
                double lobeY=baseY+(random.NextDouble()-0.5)*0.22;
                double rotation=(random.NextDouble()-0.5)*0.7,lobeHeat=Clamp(heat+(random.NextDouble()-0.5)*0.16,0,1);
                double centerLobeX=centerX+lobeX[l]-mid;
                for(int j=0;j<lobeMembers[l];j++) {
                    double angle=j*2.3999632297,r=Math.Sqrt((j+0.5)/lobeMembers[l]);
                    double localX=Math.Cos(angle)*r*lobeA[l],localY=Math.Sin(angle)*r*lobeB[l];
                    double px=centerLobeX+localX*Math.Cos(rotation)-localY*Math.Sin(rotation);
                    double py=lobeY+localX*Math.Sin(rotation)+localY*Math.Cos(rotation);
                    if(resting) {
                        // Turn the base reservoir with the lamp: wax wets the left end,
                        // not the lower long wall. Its lobes extend across the channel.
                        px=0.04+lobeB[l]+localY;
                        py=0.5+(lobeX[l]-mid+localX)/Math.Max(1,span/0.85);
                    }
                    Particles[index++]=new Particle {X=Clamp(px,0.04,Width-0.04),Y=Clamp(py,0.04,0.96),Heat=lobeHeat,Color=color};
                }
            }
        }
        // Small satellite drops between the big masses, like the little beads in the real lamp.
        for(int s=0;s<satelliteCount;s++) {
            int members=satelliteSizes[s];
            double centerX=(s+0.2+random.NextDouble()*0.6)*Width/satelliteCount,centerY=0.25+random.NextDouble()*0.35;
            double heat=0.2+random.NextDouble()*0.6,color=random.Next(6),radius=Math.Sqrt(members)*0.045;
            for(int j=0;j<members;j++) {
                double angle=j*2.3999632297,r=radius*Math.Sqrt((j+0.5)/members);
                Particles[index++]=new Particle {X=Clamp(centerX+Math.Cos(angle)*r*1.2,0.04,Width-0.04),Y=Clamp(centerY+Math.Sin(angle)*r/1.2,0.04,0.96),Heat=heat,Color=color};
            }
        }
        hashWidth=(int)Math.Ceiling(Width/Support)+1;hashHeight=(int)Math.Ceiling(1/Support)+1;
        buckets=new List<int>[hashWidth*hashHeight];for(int i=0;i<buckets.Length;i++)buckets[i]=new List<int>(24);
        // Relax the initial wax distribution, so opening a photo starts in full flow.
        for(int i=0;i<72;i++)Step(1.0/60);
    }
    // Build the steady base heat profile once; circulation evolves from thermal forces.
    void UpdateHeaters() {
        for(int x=0;x<nx;x++) {
            double strength=0;
            for(int i=0;i<heaters.Length;i++){double d=(double)x/(nx-1)-heaters[i];strength+=power[i]*Math.Exp(-d*d*HeaterWidth);}
            profile[x]=Math.Min(1,strength);
        }
    }
    double Sample(double[] data,double px,double py) {
        double gx=Clamp(px/dx,0,nx-1.001),gy=Clamp(py/dy,0,ny-1.001);
        int x=(int)gx,y=(int)gy,at=y*nx+x;double fx=gx-x,fy=gy-y;
        return (data[at]*(1-fx)+data[at+1]*fx)*(1-fy)+(data[at+nx]*(1-fx)+data[at+nx+1]*fx)*fy;
    }
    double ProfileAt(double transverse) {
        double gx=Clamp(transverse*(nx-1),0,nx-1.001);int x=(int)gx;double f=gx-x;
        return profile[x]*(1-f)+profile[x+1]*f;
    }
    public double LiquidColor(double px,double py) {return Sample(dye,px,py);}
    void LiquidReaction(Particle p,double impulseX,double impulseY) {
        // Bilinear deposition is the adjoint of sampling. Drag transfers the opposite
        // momentum to the carrier, so rising and sinking wax generates local wakes.
        // 0.18 is the dimensionless particle-to-grid-cell mass ratio of this reduced model.
        double gx=Clamp(p.X/dx,0,nx-1.001),gy=Clamp(p.Y/dy,0,ny-1.001);
        int x=(int)gx,y=(int)gy,at=y*nx+x;double fx=gx-x,fy=gy-y;
        oldU[at]-=impulseX*(1-fx)*(1-fy)*0.18;oldV[at]-=impulseY*(1-fx)*(1-fy)*0.18;
        oldU[at+1]-=impulseX*fx*(1-fy)*0.18;oldV[at+1]-=impulseY*fx*(1-fy)*0.18;
        oldU[at+nx]-=impulseX*(1-fx)*fy*0.18;oldV[at+nx]-=impulseY*(1-fx)*fy*0.18;
        oldU[at+nx+1]-=impulseX*fx*fy*0.18;oldV[at+nx+1]-=impulseY*fx*fy*0.18;
    }
    void Boundaries() {
        for(int y=0;y<ny;y++){u[y*nx]=u[y*nx+nx-1]=0;v[y*nx]=v[y*nx+1];v[y*nx+nx-1]=v[y*nx+nx-2];}
        for(int x=0;x<nx;x++){v[x]=v[(ny-1)*nx+x]=0;u[x]=u[nx+x];u[(ny-1)*nx+x]=u[(ny-2)*nx+x];}
    }
    void MoveLiquid(double dt) {
        Array.Copy(u,oldU,u.Length);Array.Copy(v,oldV,v.Length);
        Array.Copy(temperature,oldTemperature,temperature.Length);Array.Copy(dye,oldDye,dye.Length);
        for(int y=1;y<ny-1;y++)for(int x=1;x<nx-1;x++) {
            int at=y*nx+x;double px=x*dx,py=y*dy;
            double backX=px-dt*oldU[at],backY=py-dt*oldV[at];
            u[at]=Sample(oldU,backX,backY)*0.9985;
            v[at]=Sample(oldV,backX,backY)*0.9985;
            double t=Sample(oldTemperature,backX,backY);
            // Thermal diffusion and buoyancy; pressure removes compressive motion.
            t+=dt*0.22*((oldTemperature[at-1]+oldTemperature[at+1]+oldTemperature[at-nx]+oldTemperature[at+nx])*0.25-t);
            temperature[at]=Clamp(t,0,1);u[at]+=dt*CarrierBuoyancy*(t-0.5);
            dye[at]=Sample(oldDye,backX,backY);
        }
        // Long side walls are insulated; the warm base and cold top are now left/right.
        for(int x=0;x<nx;x++) {
            temperature[x]=temperature[nx+x];temperature[(ny-1)*nx+x]=temperature[(ny-2)*nx+x];
            dye[x]=dye[nx+x];dye[(ny-1)*nx+x]=dye[(ny-2)*nx+x];
        }
        for(int y=0;y<ny;y++){temperature[y*nx]=Clamp(0.55+0.4*ProfileAt(y*dy),0,1);temperature[y*nx+nx-1]=0.08;dye[y*nx]=dye[y*nx+1];dye[y*nx+nx-1]=dye[y*nx+nx-2];}
        Boundaries();Array.Clear(pressure,0,pressure.Length);
        double ix=1/(dx*dx),iy=1/(dy*dy),denominator=2*(ix+iy);
        for(int y=1;y<ny-1;y++)for(int x=1;x<nx-1;x++) {
            int at=y*nx+x;divergence[at]=((u[at+1]-u[at-1])/(2*dx)+(v[at+nx]-v[at-nx])/(2*dy))/dt;
        }
        for(int iteration=0;iteration<22;iteration++) {
            for(int y=1;y<ny-1;y++)for(int x=1;x<nx-1;x++) {
                int at=y*nx+x;pressure[at]=((pressure[at-1]+pressure[at+1])*ix+(pressure[at-nx]+pressure[at+nx])*iy-divergence[at])/denominator;
            }
            for(int x=0;x<nx;x++){pressure[x]=pressure[nx+x];pressure[(ny-1)*nx+x]=pressure[(ny-2)*nx+x];}
            for(int y=0;y<ny;y++){pressure[y*nx]=pressure[y*nx+1];pressure[y*nx+nx-1]=pressure[y*nx+nx-2];}
        }
        for(int y=1;y<ny-1;y++)for(int x=1;x<nx-1;x++) {
            int at=y*nx+x;u[at]-=dt*(pressure[at+1]-pressure[at-1])/(2*dx);v[at]-=dt*(pressure[at+nx]-pressure[at-nx])/(2*dy);
        }
        Boundaries();
    }
    void BuildHash() {
        foreach(var bucket in buckets)bucket.Clear();
        for(int i=0;i<Particles.Length;i++) {
            Particle p=Particles[i];int x=(int)Clamp(p.X/Support,0,hashWidth-1),y=(int)Clamp(p.Y/Support,0,hashHeight-1);
            buckets[y*hashWidth+x].Add(i);
        }
    }
    int Neighbors(int index) {
        Particle p=Particles[index];int x=(int)(p.X/Support),y=(int)(p.Y/Support),count=0;
        for(int gy=Math.Max(0,y-1);gy<=Math.Min(hashHeight-1,y+1);gy++)for(int gx=Math.Max(0,x-1);gx<=Math.Min(hashWidth-1,x+1);gx++) {
            foreach(int j in buckets[gy*hashWidth+gx])if(j!=index) {
                Particle q=Particles[j];double rx=q.X-p.X,ry=q.Y-p.Y,d2=rx*rx+ry*ry;
                if(d2<Support*Support&&d2>1e-12){neighbors[count]=j;distances[count++]=Math.Sqrt(d2);}
            }
        }
        return count;
    }
    public void Step(double dt) {
        if(dt<=0||dt>1.0/30)throw new ArgumentOutOfRangeException("dt");
        elapsed+=dt;MoveLiquid(dt);BuildHash();
        // MoveLiquid has finished using its advection scratch buffers. Reuse them for
        // all drag reactions, then apply together to avoid particle-order feedback.
        Array.Clear(oldU,0,oldU.Length);Array.Clear(oldV,0,oldV.Length);
        // Strong pairwise viscosity makes the wax thick and syrupy: neighbours share velocity and heat,
        // so a blob rises, stretches and necks as one body instead of shedding loose particles.
        for(int i=0;i<Particles.Length;i++) {
            Particle p=Particles[i];int count=Neighbors(i);
            for(int n=0;n<count;n++) {
                int j=neighbors[n];if(j<=i)continue;Particle q=Particles[j];
                double viscosity=WaxViscosity*(1.8-0.8*(p.Heat+q.Heat)*0.5);
                double amount=dt*viscosity*(1-distances[n]/Support),vx=(q.VX-p.VX)*amount,vy=(q.VY-p.VY)*amount;
                p.VX+=vx;p.VY+=vy;q.VX-=vx;q.VY-=vy;
                double exchange=(q.Heat-p.Heat)*dt*WaxConduction*(1-distances[n]/Support);p.Heat+=exchange;q.Heat-=exchange;
            }
        }
        foreach(Particle p in Particles) {
            p.OldX=p.X;p.OldY=p.Y;
            // Same thermal conditions as an upright lamp, rotated: wax warms at the
            // left base and cools at the right top, retaining heat during its journey.
            double heater=0.12+0.88*ProfileAt(p.Y);
            double heating=4.0*heatRate*Math.Exp(-8*p.X)*heater*(1-p.Heat);
            double cooling=coolRate*(0.06+3.2*Math.Exp(-8*(Width-p.X)))*p.Heat;
            p.Heat=Clamp(p.Heat+dt*(heating-cooling),0,1);
            double impulseX=(Sample(u,p.X,p.Y)*SideFollow-p.VX)*dt*WaxDrag;
            double impulseY=(Sample(v,p.X,p.Y)-p.VY)*dt*WaxDrag;
            LiquidReaction(p,impulseX,impulseY);
            p.VX+=impulseX+(p.Heat-0.5)*Buoyancy*dt;
            // A thin non-wetting layer at the end caps keeps wax from lodging there.
            // This is a smooth wall force, so a returning drop keeps its viscous shape.
            p.VX+=dt*0.08*(Math.Exp(-5*p.X)-Math.Exp(-5*(Width-p.X)));
            p.VY+=impulseY;
            p.VX=Clamp(p.VX,-MaxSideSpeed,MaxSideSpeed);p.VY=Clamp(p.VY,-MaxRiseSpeed,MaxRiseSpeed);
            p.X=Clamp(p.X+dt*p.VX,0.025,Width-0.025);p.Y=Clamp(p.Y+dt*p.VY,0.025,0.975);
        }
        for(int at=0;at<u.Length;at++){u[at]+=oldU[at];v[at]+=oldV[at];}
        Boundaries();
        // Two density/near-density sweeps produce cohesive drops and pinching filaments.
        for(int iteration=0;iteration<2;iteration++) {
            BuildHash();
            for(int i=0;i<Particles.Length;i++) {
                Particle p=Particles[i];int count=Neighbors(i);double density=0,nearDensity=0;
                for(int n=0;n<count;n++){double w=1-distances[n]/Support;density+=w*w;nearDensity+=w*w*w;}
                // Modest thermal expansion, in dimensionless simulation units.
                // Coefficients are visual approximations, not measured commercial wax properties.
                double restDensity=4.4/(1+0.10*(p.Heat-0.5));
                double pressure=18*(density-restDensity),nearPressure=32*nearDensity,shiftX=0,shiftY=0;
                // Under-dense wax (the surface) is pulled in harder: surface tension rounds and merges blobs.
                if(pressure<0)pressure*=Cohesion;
                for(int n=0;n<count;n++) {
                    Particle q=Particles[neighbors[n]];double distance=distances[n],w=1-distance/Support;
                    double shift=Clamp(dt*dt*(pressure*w+nearPressure*w*w)*0.5,-0.012,0.012);
                    double sx=(q.X-p.X)/distance*shift,sy=(q.Y-p.Y)/distance*shift;
                    q.X+=sx;q.Y+=sy;shiftX-=sx;shiftY-=sy;
                }
                p.X+=shiftX;p.Y+=shiftY;
            }
            foreach(Particle p in Particles){p.X=Clamp(p.X,0.025,Width-0.025);p.Y=Clamp(p.Y,0.025,0.975);}
        }
        foreach(Particle p in Particles) {
            p.VX=Clamp((p.X-p.OldX)/dt,-MaxSideSpeed,MaxSideSpeed)*0.995;p.VY=Clamp((p.Y-p.OldY)/dt,-MaxRiseSpeed,MaxRiseSpeed)*0.995;
        }
    }
}
}
