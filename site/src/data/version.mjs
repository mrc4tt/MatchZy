// The MatchZy version the site shows: the newest release (git tag "x.y.z"), not ModuleVersion in
// Core/MatchZy.cs. ModuleVersion is bumped before a release is made, and download links to it
// pointed at a zip that did not exist yet. Plain JS: used by plugin.ts and the remark plugin.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const repoRoot = () => resolve(process.cwd(), '..');

/** "1.0.25" -> [1, 0, 25]; null for anything else ("latest", "v1"). */
export function parseVersion(v) {
	const m = /^(\d+)\.(\d+)\.(\d+)$/.exec(String(v ?? '').trim());
	return m ? m.slice(1).map(Number) : null;
}

/** Negative, 0 or positive like a sort comparator; unparsable versions sort first. */
export function compareVersions(a, b) {
	const x = parseVersion(a) ?? [-1, -1, -1];
	const y = parseVersion(b) ?? [-1, -1, -1];
	return x[0] - y[0] || x[1] - y[1] || x[2] - y[2];
}

/** ModuleVersion in the source (may be ahead of the last release). */
export function sourceVersion(root = repoRoot()) {
	const core = readFileSync(join(root, 'Core/MatchZy.cs'), 'utf-8');
	return core.match(/ModuleVersion => "([^"]+)"/)?.[1] ?? 'dev';
}

/** The newest release tag; the source version when git or the tags are not available. */
export function releasedVersion(root = repoRoot()) {
	try {
		const tags = execFileSync('git', ['-C', root, 'tag', '-l'], { encoding: 'utf-8' })
			.split('\n')
			.filter((t) => parseVersion(t));
		if (tags.length) return tags.sort(compareVersions).at(-1);
	} catch {
		// No git (for example a build from a source archive): fall through.
	}
	return sourceVersion(root);
}
