// CHANGELOG.md and the What's new summary, parsed at build time. Both files stay the source:
// the release workflow (.github/workflows/release.yml) reads whats-new.md for the release body,
// and CHANGELOG.md is linked from every release.
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { compareVersions, releasedVersion } from './version.mjs';

const root = resolve(process.cwd(), '..');
// Also read by .github/workflows/release.yml and tools/package.sh (release body).
const WHATS_NEW = 'site/whats-new.md';

/** The newest release (git tag); sections for later versions are not released yet. */
export const latestRelease = releasedVersion(root);
export const isUnreleased = (v: string) => compareVersions(v, latestRelease) > 0;

const read = (p: string) => readFileSync(join(root, p), 'utf-8').replace(/\r\n/g, '\n');

/** Anchor for a version: "1.0.24" -> "1024", the id the old site and GitHub's CHANGELOG.md use. */
export const versionId = (v: string) => v.replace(/\./g, '');

// ---------- inline Markdown (the subset the two files use: code, bold, italic, links) ----------

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

function href(url: string, base: string) {
	if (/^(https?:|mailto:|#)/.test(url)) return url;
	const [path, hash] = url.split('#');
	const page = path.replace(/^\.\//, '').replace(/\.md$/, '');
	const map: Record<string, string> = { changelog: 'changelog/', 'whats-new': 'whats-new/', CHANGELOG: 'changelog/' };
	return base + (map[page] ?? `${page}/`) + (hash ? `#${hash}` : '');
}

/** Renders one line of Markdown to HTML. `base` is the locale root ("/" or "/da/") for page links. */
export function inline(md: string, base = '/') {
	const parts = md.split(/(`+)([^`]*?)\1/);
	let html = '';
	for (let i = 0; i < parts.length; i++) {
		if (i % 3 === 1) continue; // the backtick run
		if (i % 3 === 2) { html += `<code>${esc(parts[i])}</code>`; continue; }
		html += esc(parts[i])
			.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, t, u) => `<a href="${href(u.replace(/&amp;/g, '&'), base)}">${t}</a>`)
			.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
			.replace(/(^|[^*\w])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>');
	}
	return html;
}

// ---------- CHANGELOG.md ----------

export interface Release {
	version: string;
	date: string;
	items: string[]; // Markdown, one bullet each
}

export function changelog() {
	const lines = read('CHANGELOG.md').split('\n');
	const intro: string[] = [];
	const releases: Release[] = [];
	let cur: Release | null = null;
	for (const raw of lines) {
		const line = raw.trimEnd();
		const ver = line.match(/^# (\d+\.\d+\.\d+\S*)\s*$/);
		if (ver) { cur = { version: ver[1], date: '', items: [] }; releases.push(cur); continue; }
		if (/^# /.test(line)) continue; // the "# Changelog" title
		if (!cur) { if (line) intro.push(line); continue; }
		const date = line.match(/^#{2,6}\s+(.+)$/);
		if (date) { cur.date = date[1]; continue; }
		if (line.startsWith('- ')) cur.items.push(line.slice(2));
		else if (/^\s+\S/.test(line) && cur.items.length) cur.items[cur.items.length - 1] += ' ' + line.trim();
		else if (line) throw new Error(`CHANGELOG.md: unexpected line under ${cur.version}: ${line}`);
	}
	return { intro: intro.join(' '), releases };
}

// ---------- whats-new.md ----------

export const KINDS = ['New', 'Changed', 'Fixed', 'Security', 'Removed'] as const;
export type Kind = (typeof KINDS)[number];

export interface Summary {
	version: string;
	date: string;
	open: boolean; // a "## x.y.z" section; "??? note" ones are listed under older releases
	action: string[]; // the "Action needed" box, one bullet each
	items: { kind: Kind | null; text: string }[];
}

export function whatsNew() {
	const lines = read(WHATS_NEW).split('\n');
	const intro: string[] = [];
	const releases: Summary[] = [];
	let cur: Summary | null = null;
	let inAction = false;
	for (const raw of lines) {
		const line = raw.trimEnd();
		const open = line.match(/^## (\d+\.\d+\.\d+\S*)\s*(?:<small>(.*)<\/small>)?\s*$/);
		const closed = line.match(/^\?\?\?\+? \w+ "(\d+\.\d+\.\d+\S*)\s*-\s*(.*)"\s*$/);
		if (open || closed) {
			const m = (open ?? closed)!;
			cur = { version: m[1], date: m[2] ?? '', open: !!open, action: [], items: [] };
			releases.push(cur);
			inAction = false;
			continue;
		}
		if (/^#{1,2} /.test(line)) { cur = null; continue; } // "# What's new", "## Older releases"
		if (!cur) { if (line && releases.length === 0) intro.push(line); continue; }
		const body = cur.open ? line : line.replace(/^ {4}/, '');
		if (/^!!! \w+ "Action needed"/.test(body)) { inAction = true; continue; }
		const sub = body.match(/^ {4}- (.*)$/);
		if (inAction && sub) { cur.action.push(sub[1]); continue; }
		const item = body.match(/^- (.*)$/);
		if (item) {
			inAction = false;
			const k = item[1].match(/^\*\*(\w+):\*\*\s*(.*)$/);
			const kind = k && (KINDS as readonly string[]).includes(k[1]) ? (k[1] as Kind) : null;
			cur.items.push({ kind, text: kind ? k![2] : item[1] });
			continue;
		}
		if (!body.trim()) continue;
		if (/^\S/.test(body) && !cur.open) { cur = null; continue; } // text after the collapsed blocks
		throw new Error(`${WHATS_NEW}: unexpected line under ${cur.version}: ${line}`);
	}
	return { intro: intro.join(' '), releases };
}
