// MatchZy's event classes (Core/Events.cs, Core/Get5Events.cs) as a catalog for the event explorer
// (reference/event-explorer): every event name, its format, its fields (inherited ones included)
// with type and comment, and an example payload. Ported from the retired MkDocs events_catalog.py hook.
// Plain JS so it can also be run alone: node src/data/events.mjs [repo root]
import { readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const CLASS = /^public (?:abstract |sealed )?class (\w+)(?:\s*:\s*(\w+))?/gm;
const PROP = /^\s*public (required )?([\w<>[\]?, ]+?) (\w+) \{ get;/;
const JSON_NAME = /\[JsonPropertyName\("([^"]+)"\)\]/;

// Classes whose event name is passed in by a helper rather than at "new Class(...)".
const NAME_HELPERS = {
	MatchPhaseEvent: /PublishPhaseEvent\("([a-z_]+)"/g,
	Get5BombCarrierEvent: /SendBombCarrierEvent\("([a-z_]+)"/g,
	BombCarrierLiveEvent: /SendBombCarrierEvent\("([a-z_]+)"/g,
};
// Named "...LiveEvent" but sent in both event formats.
const FORMAT_OVERRIDES = { GoingLiveEvent: 'both', RoundStartLiveEvent: 'both' };

const SAMPLES = {
	matchid: 1234, map_number: 0, round_number: 7, round_time: 41250, map_name: 'de_mirage',
	steamid: '76561198000000001', steamid64: '76561198000000001', player_steamid: '76561198000000001',
	attacker_steamid: '76561198000000001', victim_steamid: '76561198000000002', name: 'Mio',
	player_name: 'Mio', attacker_name: 'Mio', victim_name: 'Bravo1', team: 'team1',
	side: 'ct', winner: 'team1', weapon: 'ak47', plugin_version: '1.0.24',
	thread_id: '8f2c0c1e5b1a4a0e9d7c2b6a1f3e4d5c', call_id: 'c41d9a7e2f6b4e1a8b3c5d7e9f0a1b2c',
	message: 'gg', filename: '1234_0_de_mirage.dem', demo_filename: '1234_0_de_mirage.dem',
	hostname: 'Liga #3', address: '0.0.0.0:27015', reason: 'admin', kind: 'admin',
	called_at: '2026-11-09T18:02:11Z', sent_at: '2026-11-09T18:03:40Z', closed_at: '2026-11-09T18:10:00Z',
	team1_side: 't', site: 'A', pause_type: 'technical', next_map: 'de_inferno', trigger: 'changelevel',
	command: 'say', ip_address: '203.0.113.7', team_name: 'Alpha',
};

const stripComments = (s) => s.replace(/\/\*[\s\S]*?\*\//g, '');

function parseClasses(text) {
	const classes = {};
	const starts = [...text.matchAll(CLASS)];
	starts.forEach((m, i) => {
		const body = text.slice(m.index + m[0].length, i + 1 < starts.length ? starts[i + 1].index : text.length);
		const props = [];
		let comment = [];
		let jsonName = null;
		for (const line of body.split('\n')) {
			const s = line.trim();
			if (s.startsWith('//')) {
				const c = s.replace(/^\/+/, '').trim();
				if (c && ![...c].every((ch) => '═─='.includes(ch))) comment.push(c);
				continue;
			}
			const jn = s.match(JSON_NAME);
			if (jn) { jsonName = jn[1]; continue; }
			if (s.startsWith('[JsonIgnore')) continue;
			const pm = line.match(PROP);
			if (pm && jsonName) props.push([jsonName, pm[2].trim(), comment.join(' '), !!pm[1]]);
			if (s && !s.startsWith('[')) { comment = []; jsonName = null; }
		}
		classes[m[1]] = {
			base: m[2] ?? null,
			props,
			names: [...body.matchAll(/base\("([a-z_]+)"\)/g)].map((x) => x[1]),
			paramCtor: /base\(eventName\)/.test(body),
		};
	});
	return classes;
}

// Event names given where a class with an eventName constructor is created:
// new PlayerReadyChangedEvent(ready ? "player_ready" : "player_unready"), PublishPhaseEvent("x", ...).
function constructionNames(cls, sources) {
	const names = new Set();
	const re = new RegExp(`new ${cls}\\(([^;{]*?)\\)\\s*(?:\\{|;)`, 'g');
	for (const src of sources) {
		for (const m of src.matchAll(re)) for (const n of m[1].matchAll(/"([a-z_]+)"/g)) names.add(n[1]);
		if (NAME_HELPERS[cls]) for (const n of src.matchAll(NAME_HELPERS[cls])) names.add(n[1]);
	}
	return [...names].sort();
}

function allProps(cls, classes) {
	const chain = [];
	for (let c = cls; c in classes; c = classes[c].base) chain.push(c);
	const props = [];
	const seen = new Set();
	for (const c of chain.reverse()) {
		for (const p of classes[c].props) if (!seen.has(p[0])) { seen.add(p[0]); props.push(p); }
	}
	return props;
}

function derives(cls, root, classes) {
	for (let c = cls; c in classes; c = classes[c].base) if (c === root) return true;
	return false;
}

function sample(name, ctype, classes, depth = 0, parent = '') {
	const t = ctype.replaceAll('?', '').trim();
	if (parent === 'weapon' && name === 'name') return 'ak47';
	if (t === 'string' && name.endsWith('_team')) return 'CT';
	if (t === 'string' && name.endsWith('_side')) return 'ct';
	if (name in SAMPLES && ['string', 'long', 'int'].includes(t)) {
		const v = SAMPLES[name];
		return t === 'string' && typeof v !== 'string' ? String(v) : v;
	}
	if (t.startsWith('List<') || t.endsWith('[]')) {
		const inner = t.startsWith('List<') ? t.slice(5, -1) : t.slice(0, -2);
		return depth < 3 ? [sample(name, inner, classes, depth + 1, parent)] : [];
	}
	if (t.startsWith('Dictionary<')) return {};
	if (t === 'string') return name ? name.replaceAll('_', ' ') : 'text';
	if (['int', 'long', 'short'].includes(t)) return 1;
	if (['float', 'double', 'decimal'].includes(t)) return 1.5;
	if (t === 'bool') return false;
	if (t in classes && depth < 4) {
		return Object.fromEntries(allProps(t, classes).map((p) => [p[0], sample(p[0], p[1], classes, depth + 1, name)]));
	}
	return null;
}

function csFiles(dir) {
	return readdirSync(dir, { withFileTypes: true }).flatMap((d) => {
		if (d.isDirectory()) {
			if (['node_modules', '.git', 'bin', 'obj', 'tests'].includes(d.name)) return [];
			return csFiles(join(dir, d.name));
		}
		return d.name.endsWith('.cs') ? [join(dir, d.name)] : [];
	});
}

export function events(root = resolve(process.cwd(), '..')) {
	const read = (p) => readFileSync(join(root, p), 'utf-8');
	const classes = parseClasses(stripComments(read('Core/Events.cs')) + '\n' + stripComments(read('Core/Get5Events.cs')));
	const sources = csFiles(root).map((f) => readFileSync(f, 'utf-8'));
	const out = new Map();
	for (const [cls, info] of Object.entries(classes)) {
		if (cls === 'MatchZyEvent' || !derives(cls, 'MatchZyEvent', classes)) continue;
		const names = [...info.names, ...(info.paramCtor ? constructionNames(cls, sources) : [])];
		if (!names.length) continue;
		const format = FORMAT_OVERRIDES[cls]
			?? (cls.startsWith('Get5') ? 'get5'
				: cls.endsWith('LiveEvent') ? 'legacy'
					: derives(cls, 'ServerLifecycleEvent', classes) ? 'server' : 'both');
		const props = allProps(cls, classes);
		for (const name of [...new Set(names)].sort()) {
			const key = `${name}|${format}`;
			if (out.has(key)) continue;
			const example = { event: name };
			for (const p of props) if (p[0] !== 'event') example[p[0]] = sample(p[0], p[1], classes);
			out.set(key, {
				name, format, class: cls,
				fields: props.filter((p) => p[0] !== 'event').map((p) => ({ name: p[0], type: p[1], description: p[2], required: p[3] })),
				example,
			});
		}
	}
	const order = { both: 0, server: 1, get5: 2, legacy: 3 };
	return [...out.values()].sort((a, b) => order[a.format] - order[b.format] || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
	console.log(JSON.stringify(events(resolve(process.argv[2] ?? '..'))));
}
