// Playable hypothesis model. Dimensionless inventories and accelerated time;
// deliberately separate from the validated .NET thermodynamic ledger.
export class FormationDemoWorld {
    constructor(seed = 24061984, width = 128, height = 64) {
        this.seed = seed; this.width = width; this.height = height; this.time = 0;
        const n = width * height;
        for (const name of ['lava', 'crust', 'heat', 'bed', 'uplift', 'rift', 'compression', 'cooling', 'reservoir', 'flux']) this[name] = new Float64Array(n);
        let state = seed >>> 0;
        const random = () => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state / 4294967296; };
        const noise = (x, y, z) => {
            const ix = Math.floor(x), iy = Math.floor(y), iz = Math.floor(z);
            const smooth = t => t * t * (3 - 2 * t);
            const fx = smooth(x - ix), fy = smooth(y - iy), fz = smooth(z - iz);
            let value = 0;
            for (let dz = 0; dz < 2; dz++) for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) {
                let h = Math.imul(ix + dx, 374761393) ^ Math.imul(iy + dy, 668265263) ^ Math.imul(iz + dz, 2147483647) ^ seed;
                h = Math.imul(h ^ (h >>> 13), 1274126177); h = (h ^ (h >>> 16)) >>> 0;
                value += (h / 4294967296) * (dx ? fx : 1 - fx) * (dy ? fy : 1 - fy) * (dz ? fz : 1 - fz);
            }
            return value;
        };
        const plates = Array.from({ length: 18 }, () => {
            const y = random() * 2 - 1, a = random() * Math.PI * 2, r = Math.sqrt(1 - y * y);
            return { x: r * Math.cos(a), y, z: r * Math.sin(a), cold: random(), convergent: random() > .5 };
        });
        for (let j = 0; j < height; j++) for (let i = 0; i < width; i++) {
            const k = j * width + i, y = 1 - 2 * (j + .5) / height, a = (i + .5) / width * Math.PI * 2;
            const r = Math.sqrt(1 - y * y), x = r * Math.cos(a), z = r * Math.sin(a);
            const ranked = plates.map(p => ({ p, d: 1 - (x * p.x + y * p.y + z * p.z) })).sort((a, b) => a.d - b.d);
            const boundary = Math.exp(-Math.pow((ranked[1].d - ranked[0].d) * 30, 2));
            const folded = 2 * (.55 * noise(x * 5, y * 5, z * 5) + .3 * noise(x * 13, y * 13, z * 13)
                + .15 * noise(x * 29, y * 29, z * 29)) - 1;
            this.compression[k] = ranked[0].p.convergent ? boundary : 0;
            this.rift[k] = boundary * (ranked[0].p.convergent ? .35 : 1);
            this.bed[k] = .16 * folded + .25 * ranked[0].d - .12 * boundary;
            this.cooling[k] = .001 + .020 * ranked[0].p.cold + .002 * (folded + 1);
            this.lava[k] = 1; this.heat[k] = 1.05 + .35 * ranked[0].p.cold + .12 * folded;
            this.reservoir[k] = 2;
        }
        this._massDelta = new Float64Array(n); this._heatDelta = new Float64Array(n);
        this.initialInventory = this.inventory();
    }
    inventory() { return this.lava.reduce((s, x, i) => s + x + this.crust[i] + this.reservoir[i], 0); }
    advance(seconds) {
        if (!Number.isFinite(seconds) || seconds < 0 || seconds > 300) throw new RangeError('Demo step must be 0–300 seconds.');
        const endTime = this.time + seconds;
        // Bounded demonstrator dynamics: a one-unit step remains inside the explicit
        // 20%-per-donor face-flux cap, and is regression-checked against 0.1-unit refinement.
        const maximumTick = 1;
        const ticks = Math.ceil(seconds / maximumTick), dt = ticks ? seconds / ticks : 0;
        for (let t = 0; t < ticks; t++) this.tick(dt);
        this.time = endTime;
        return this.stats();
    }
    tick(dt) {
        const n = this.lava.length, dm = this._massDelta, dh = this._heatDelta;
        dm.fill(0); dh.fill(0); this.flux.fill(0);
        const exchange = (a, b) => {
            const headA = this.bed[a] + this.uplift[a] + this.crust[a] + this.lava[a];
            const headB = this.bed[b] + this.uplift[b] + this.crust[b] + this.lava[b];
            const donor = headA > headB ? a : b, recipient = donor === a ? b : a;
            // Each donor has at most four edges. Limit summed withdrawals to <=20%.
            const mobile = 1 / (1 + 25 * this.crust[donor]);
            const mass = Math.min(this.lava[donor] * .05, Math.abs(headA - headB) * mobile * dt * .8);
            const heat = mass * this.heat[donor];
            dm[donor] -= mass; dm[recipient] += mass; dh[donor] -= heat; dh[recipient] += heat;
            this.flux[donor] += mass / dt; this.flux[recipient] += mass / dt;
        };
        for (let j = 0; j < this.height; j++) for (let i = 0; i < this.width; i++) {
            const k = j * this.width + i;
            exchange(k, j * this.width + (i + 1) % this.width);
            if (j + 1 < this.height) exchange(k, k + this.width);
        }
        for (let k = 0; k < n; k++) {
            const old = this.lava[k], next = old + dm[k];
            this.heat[k] = next > 1e-12 ? (old * this.heat[k] + dh[k]) / next : this.heat[k];
            this.lava[k] = next;
            const feed = Math.min(this.reservoir[k], dt * .012 * Math.pow(this.rift[k], 6));
            this.reservoir[k] -= feed;
            if (feed > 0) {
                this.heat[k] = (this.heat[k] * this.lava[k] + feed * 1.8) / (this.lava[k] + feed);
                this.lava[k] += feed;
            }
            this.heat[k] = Math.max(.1, this.heat[k] - dt * this.cooling[k] / (1 + this.crust[k] * 8));
            const freeze = Math.min(this.lava[k], dt * Math.max(0, 1 - this.heat[k]) * .12);
            this.lava[k] -= freeze; this.crust[k] += freeze;
            // A simplified rupture/remelt rule; a fracture exposes owned liquid.
            if (this.heat[k] > 1.05 && this.crust[k] > 0 && this.lava[k] > 0) {
                // Hot owned melt must pay for remelting nearby solid; it cannot do so for free.
                // The latent-cost proxy is illustrative, not a calibrated thermodynamic equation of state.
                const moltenMass = this.lava[k], excess = (this.heat[k] - 1.05) * moltenMass;
                const remelt = Math.min(this.crust[k], excess / .9, dt * (this.heat[k] - 1.05) * .1);
                if (remelt > 0) {
                    this.crust[k] -= remelt; this.lava[k] += remelt;
                    this.heat[k] = Math.max(.1, (this.heat[k] * moltenMass - .9 * remelt) / this.lava[k]);
                }
            }
            // Uncalibrated convergence hypothesis, visually exaggerated by the renderer.
            this.uplift[k] += dt * .006 * this.crust[k] * this.compression[k];
        }
        this.time += dt;
    }
    stats() {
        const coverage = this.crust.reduce((s, x) => s + Math.min(1, x / .18), 0) / this.crust.length;
        return { seconds: this.time, coverage, inventoryResidual: this.inventory() - this.initialInventory,
            phase: coverage < .05 ? 'Magma ocean' : coverage < .65 ? 'Crust islands · active lava' : 'Growing land · volcanic rifts' };
    }
}
