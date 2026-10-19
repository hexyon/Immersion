# Lava Flow: research, model, and validation

The effect simulates an upright lava lamp in coordinates rotated 90 degrees into the viewer's 28-pixel horizontal strip: the physical base maps to the left end and the physical top maps to the right end. It uses slow thermal circulation, conserved wax particles, viscous deformation, and an implicitly reconstructed wax surface. It begins with molten wax already in circulation, rather than making the viewer wait through a physical lamp's startup.

## What the research supports

Research reviewed on 19 October 2026:

1. [Mathmos: frequently asked questions](https://mathmos.com/faq/) explains the commercial lamp's mechanism: wax warms and expands near the base, rises, cools, and sinks. It describes slow, smooth motion and explains that lamp temperature and the correct bulb affect operation. Its reported one to one-and-a-half-hour warmup (sometimes three hours for a new bottle) is a startup time, not the duration of one blob's journey.
2. [LAVA: manufacturer FAQ](https://lavalamp.com/faq/) independently describes the heating/cooling density reversal, a wax reservoir, and pillar shapes during startup. It specifies two to six hours for initial startup. Neither manufacturer's guidance establishes a universal operating blob speed in pixels/second or a universal cycle period.
3. [Gyure and Janosi, Basics of lava-lamp convection, Physical Review E 80, 046307 (2009)](https://doi.org/10.1103/PhysRevE.80.046307) studies an immiscible laboratory system. Warm blobs detach, rise, remain at the cooled boundary for a while, then return after cooling. Its slow regime is limited by heat transport, and another regime is viscosity limited. The experimental materials are silicone fluid and salt solution; their timing must not be presented as a calibration for commercial wax. The important model implication is thermal memory and boundary dwell, rather than periodic random pushes.
4. [Muller, Solenthaler, Keiser and Gross, Particle-Based Fluid-Fluid Interaction (2005)](https://cgl.ethz.ch/Downloads/Publications/Papers/2005/Mue05a/Mue05a.pdf), especially sections 3.2, 3.3 and 4.2, models immiscibility with interface tension, temperature diffusion, and temperature-dependent density. Its lava-lamp example heats the ground and cools the ceiling. Its numerical constants and frame rate describe an illustrative simulation, not measured real-lamp motion.
5. [Clavet, Beaudoin and Poulin, Particle-based Viscoelastic Fluid Simulation (2005)](https://diglib.eg.org/items/7cad7994-b781-40ce-ad04-0bf61dc94279) supplies the existing double-density relaxation approach used for volume preservation, cohesive bodies, and necks. The optional elastic springs are not implemented.
6. [Stam, Stable Fluids (1999)](https://www.dgp.toronto.edu/public_user/stam/reality/Research/pdf/ns.pdf) supplies the existing semi-Lagrangian carrier advection and pressure-projection approach.

The implementation choices below are engineering interpretations of these mechanisms. They are not measured commercial wax coefficients. In particular, claiming an exact real-life speed from these sources would be unjustified.

## Implemented behaviour

- **Moderate pace:** `FlowSpeed=1.65`; a modest display speed-up keeps motion perceptible in the small strip. Fixed 1/60-second substeps advance 1.65 simulation seconds per wall-clock second under normal load. Speed bounds are 0.16 channel-depth units/second along the lamp axis and 0.075 across it, as stability guards. At 840 by 28 pixels and the usual simulation width of 21, these correspond to about 10.6 horizontal and 3.5 transverse pixels/second; actual motion is generally slower and includes pauses. These are authored display targets, not experimentally measured speeds.
- **Rotated upright-lamp conditions:** the heated base is the left end and the cooled top is the right end. The long side walls have insulated thermal boundaries. Upward buoyancy maps to positive screen X; downward gravity maps to negative screen X. Both wax and carrier-liquid buoyancy use this axis, and the initial divergence-free circulation is rotated with it. No oscillating heaters, periodic power pulses, per-frame randomness, or synthetic eddy forcing are used. This is a rotation of the simulated upright-lamp coordinate system; it is not a claim that physically laying a lamp on its side changes gravity.
- **Thermal inertia:** slow heating near the left base, strong cooling at the right top, weak bulk heat loss, and heat conduction between touching wax particles. Blobs carry their temperature along the lamp axis as they travel. Boundary residence permits thermal reversal rather than bouncing them on a preset path.
- **Viscous wax:** pairwise velocity sharing increases as wax cools. Cooler material resists relative motion more strongly. A small temperature-dependent rest-density change represents expansion while particle count conserves wax mass.
- **Connected liquid motion:** sampled carrier velocity drags wax; the equal and opposite drag impulse is deposited bilinearly into the carrier grid using a dimensionless particle/cell mass ratio. Wakes and return flow can therefore respond to the moving wax. Reactions are accumulated before application to avoid particle-order feedback.
- **Mixed shapes and sizes:** a restrained number of independently seeded bodies, occasional small satellites, and overlapping lobes with varying radii, aspect ratios, temperatures, and initial locations. The reservoir wax starts against the left base rather than against the lower long wall. Density relaxation and cohesion support stretching, connecting necks, separation, rounding, and joining as local forces permit; there is no timed split or merge animation.
- **Glass interaction:** bounded positions, closed carrier boundaries, and a soft end-cap repulsion reduce end-wall trapping.

`LavaStrip` reconstructs the wax using compact poly6 kernels, photo-sampled colors, soft volume shading, and restrained backlighting strongest at the left heated base. Palette transitions still take two seconds. A deterministic photo-content seed changes the composition while keeping repeatable verification possible.

## Scope and limitations

This is a reduced 2D visual simulation, not a calibrated 3D multiphase CFD calculation or an exact reproduction of a particular lamp. It does not model wax melting/freezing, chemical surfactants, fully resolved films between colliding drops, out-of-plane motion, or the hours-long cold startup. Cooling-dependent viscosity and expansion are dimensionless approximations. Pressure relaxation provides approximate incompressibility; surface reconstruction can change apparent screen area while particle mass remains fixed. The projected carrier and wax use different resolutions, so the drag exchange is an approximate coupling rather than a proof of globally exact momentum conservation.

The coordinate rotation, prewarmed initial state, and six photo colors are presentation choices. The numerical equations remain a reduced fluid model; rotating their axes does not provide measured material calibration. The cap is 1500 wax particles; normal counts are much lower. Rendering is bounded to 960 by 42 pixels, catch-up to eight fixed substeps, and hidden animation stops its timer.

## Verification

Build with `build.ps1`. Run `Immersion.exe --self-test` for viewer integration. Run `tests/verify-lava.ps1` for reproducible seeds, actual transport and non-rigid deformation, both directions of circulation, conserved particle mass, bounded finite state over two simulated minutes, and narrow/wide aspect ratios.

Slow-flow regressions test movement over a 90-second thermal window rather than requiring a high instantaneous velocity during a natural pause. They also check substantial travel along the strip, rightward ascent and leftward descent, a gentle speed envelope, thermal inertia, and opposite axial responses of identically seeded hot and cold wax. A boundary test checks heating at the left and cooling at the right, and a three-minute cycle test tracks the same wax from warm rightward travel through cooling to leftward return. Motion samples and a contact sheet are written to `test-output/` for visual review.

## Coordinate correspondence

| Upright lamp | Horizontal strip |
| --- | --- |
| Heated bottom/base | Left end |
| Cooled top | Right end |
| Upward buoyancy | Rightward force |
| Downward gravity / cool wax descent | Leftward force |
| Side-to-side displacement | Displacement across strip thickness |
| Base reservoir | Wax against left end |
| Light beneath reservoir | Light strongest at left end |

The same density, viscosity, heat exchange, advection and pressure terms are evaluated in this rotated coordinate system. Wax paths are not rotated after the fact; forces, temperatures, boundary conditions, initial reservoir and lighting all use the correspondence above.