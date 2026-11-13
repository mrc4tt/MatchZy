// Values read from the plugin source at build time, so the site always matches this checkout:
// the version (Core/MatchZy.cs) and the landing page numbers. Same rules as the old MkDocs hook.
import { readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { releasedVersion, sourceVersion } from './version.mjs';
import { events as eventCatalog } from './events.mjs';

// The repository root: the site lives in <repo>/site and is built from there (npm run build).
const root = resolve(process.cwd(), '..');
const read = (p: string) => readFileSync(join(root, p), 'utf-8');

const core = read('Core/MatchZy.cs');

// The newest release (git tag), so the download link always exists; sourceVersion is
// ModuleVersion, which runs ahead while a release is being prepared.
export const version = releasedVersion(root);
export const sourceVersionNumber = sourceVersion(root);
export const stats = {
	// Commented-out entries ("//{ ".x", ...") are not live commands.
	commands: new Set(core.split('\n').filter((l) => !l.trim().startsWith('//'))
		.flatMap((l) => [...l.matchAll(/\{ "\.([a-z0-9_]+)",/g)].map((m) => m[1]))).size,
	settings: [...read('Config/ConfigConvars.cs').matchAll(/new\("matchzy_/g)].length,
	// Unique names from the event explorer's catalog (both formats), not only base("x") calls.
	events: new Set(eventCatalog(root).map((e) => e.name)).size,
	languages: readdirSync(join(root, 'lang')).filter((f) => f.endsWith('.json')).length,
};
export const repo = 'https://github.com/mrc4tt/MatchZy';
export const download = `${repo}/releases/download/${version}/MatchZy-${version}.zip`;

// "New" marker for pages with new_in in their front matter, while that version is the current one
// or at most NEW_IN_PATCHES patch releases older (same rule as the old site).
const NEW_IN_PATCHES = 2;
export function isRecent(newIn: string | undefined) {
	if (!newIn) return false;
	const cur = version.split('.').map(Number);
	const add = newIn.split('.').map(Number);
	if (cur.length < 3 || add.length < 3 || cur[0] !== add[0] || cur[1] !== add[1]) return false;
	const d = cur[2] - add[2];
	return d >= 0 && d <= NEW_IN_PATCHES;
}
