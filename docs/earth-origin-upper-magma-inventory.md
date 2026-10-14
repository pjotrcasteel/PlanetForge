# Funded upper-magma inventory checkpoint

This research reference addresses the released reference's roughly seven-metre active shell. `CreateUpperMagmaInitial` accepts an equivalent uniform liquid depth within the existing depth/radius <= 0.001 geometry envelope. It does not alter the released formation default.

The active mass is 4 pi R² depth rho_liquid, with the existing 2800 kg/m³ material density. The additional active mass is subtracted from the original interior inventory. Surface enthalpy uses the existing 2200 K material law; remaining interior enthalpy is the original combined enthalpy minus the new surface enthalpy. The resulting interior temperature is resolved by its new mass and enthalpy, rather than held at 2400 K by adding heat. Combined initial rock mass and energy remain unchanged. This is a hypothetical initial partition, not a geological reconstruction.

The resolved research factory uses 64 layers and quartic mass/depth grading. At one kilometre depth the first liquid cell is about 0.06 mm thick. Quadratic grading would put approximately 0.24 m in the first cell; reconstructing its radiating face would spuriously make an initially hot uniform shell look cold. Quartic grading resolves that initial boundary with finite mass and the existing implicit solver. The 128-layer cap and thin geometry restriction remain in force.

Validation checks inventory funding, phase, conserved initial energy, geometry rejection, initial radiating-face temperature, and an accepted cooling step with conservation and immutable publication. This is not yet a depth/time convergence demonstration. Internal convective transport and mechanical crust remain outstanding. No new lava shader, mountain map, default activation, merge, or live deployment is implied.
