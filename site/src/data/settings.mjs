// The settings of cfg/config.cfg for the settings generator (reference/config-generator):
// section, name, default, type, description. Ported from the retired MkDocs values.py hook.
// Plain JS so it can also be run alone: node src/data/settings.mjs [repo root]
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const SETTING = /^(matchzy_\w+|get5_\w+)\s+(.*?)\s*$/;
const OPTIONAL = /^\/\/\s*(matchzy_\w+|get5_\w+)\s+("[^"]*"|\S+)\s*$/;

const unquote = (v) => (v.length >= 2 && v[0] === '"' && v.at(-1) === '"' ? v.slice(1, -1) : v);

/**
 * Each setting: a comment block, a "Default: x" line, then the setting (or a commented-out example
 * such as // matchzy_demo_upload_url "https://...").
 * @param {string} text
 */
export function parseConfigCfg(text) {
	const settings = [];
	let section = '';
	let comment = [];
	// Several commented-out examples under one comment (matchzy_demo_upload_url / _header_key /
	// _header_value): the comment belongs to the first; the others point to it.
	let sharedFrom = null;
	const lines = text.split(/\r?\n/);
	lines.forEach((raw, i) => {
		const line = raw.trim();
		if (!line) { comment = []; sharedFrom = null; return; }
		if (line.startsWith('// ====')) return;
		const prevRule = i > 0 && lines[i - 1].trim().startsWith('// ====');
		const nextRule = i + 1 < lines.length && lines[i + 1].trim().startsWith('// ====');
		if (prevRule && nextRule) { section = line.slice(2).trim(); comment = []; return; }
		const active = line.match(SETTING);
		const optional = active ? null : line.match(OPTIONAL);
		if (active || optional) {
			const m = active ?? optional;
			const name = m[1];
			const value = unquote(m[2]);
			let def = null;
			const desc = [];
			for (const c of comment) {
				const d = c.match(/^Default:\s*(.*)$/);
				if (d) def = unquote(d[1].trim());
				else desc.push(c);
			}
			def = active && def === null ? value : (def ?? '');
			const type = def === 'true' || def === 'false' ? 'bool' : /^-?\d+(\.\d+)?$/.test(def) ? 'number' : 'text';
			let description = desc.join(' ').replace(/\s*Syntax:$/, '');
			if (optional && sharedFrom) description = `Goes with ${sharedFrom} (see there).`;
			else if (optional) sharedFrom = name;
			settings.push({ section, name, default: def, type, optional: !!optional, example: optional ? value : '', description });
			if (active) comment = [];
			return;
		}
		if (line.startsWith('//')) comment.push(line.slice(2).trim());
	});
	return settings;
}

export function settings(root = resolve(process.cwd(), '..')) {
	return parseConfigCfg(readFileSync(join(root, 'cfg/config.cfg'), 'utf-8'));
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
	console.log(JSON.stringify(settings(resolve(process.argv[2] ?? '..'))));
}
