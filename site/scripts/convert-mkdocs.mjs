#!/usr/bin/env node
// One-way converter: documentation/docs (MkDocs Material) -> site/src/content/docs (Starlight).
// Run from site/: `npm run convert`. Re-runnable until the MkDocs site is retired: it owns the
// getting-started/, guides/ and reference/ folders (English root and da/) and rewrites them.
// The landing pages (index.mdx), changelog.mdx and whats-new.mdx are hand-made and left alone
// (the last two render CHANGELOG.md and whats-new.md through components).
//
// MkDocs syntax handled:
//   !!! type "Title"         -> :::note[Title] aside (note, tip, warning->caution, danger)
//   ??? type "Title"         -> <details><summary>
//   === "Label"              -> <Tabs><TabItem> (page becomes .mdx)
//   <div class="grid cards"> -> <CardGrid><LinkCard> (page becomes .mdx)
//   <div class="mzt-tool ..."> -> <Tool name="..." /> (page becomes .mdx)
//   ## Heading { #id }       -> ## Heading {#id} (remark-heading-id.mjs keeps the id)
//   ++shift+e++              -> <kbd>Shift</kbd>+<kbd>E</kbd>
//   :material-x: icons, { .class } attribute lists -> removed
//   [x](../guides/foo.md#a)  -> [x](/guides/foo/#a) (and /da/... on Danish pages)
//   @@MATCHZY_VERSION@@      -> kept, filled in at build time (src/plugins/remark-docs.mjs)
//   x.da.md                  -> da/x.md
//   front matter: title (or the first # heading), hide: toc -> tableOfContents: false,
//   new_in kept, sidebar label + order from the mkdocs.yml nav (Danish via nav_translations).
// Anything it cannot convert is listed as a warning at the end; the exit code is 1 when there are any.

import { readFileSync, writeFileSync, mkdirSync, rmSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve, posix } from 'node:path';

const site = resolve(import.meta.dirname, '..');
const mkdocsRoot = resolve(site, '../documentation');
const src = join(mkdocsRoot, 'docs');
const out = join(site, 'src/content/docs');
const OWNED = ['getting-started', 'guides', 'reference'];
const SKIP = new Set(['index.md', 'index.da.md', 'changelog.md', 'whats-new.md']);

const warnings = [];
let sink = warnings; // pass 1 (MDX detection) writes to a throwaway list
const warn = (file, msg) => sink.push(`${file}: ${msg}`);

// ---------- mkdocs.yml nav (parsed by hand: the file has !!python tags a YAML lib rejects) ----------

function readNav() {
	const lines = readFileSync(join(mkdocsRoot, 'mkdocs.yml'), 'utf-8').split('\n');
	const nav = new Map(); // "guides/coaching.md" -> { label, order }
	const da = new Map(); // English label -> Danish label
	let section = null;
	let order = 0;
	let inTranslations = false;
	for (const line of lines) {
		if (/^nav:\s*$/.test(line)) { section = 'nav'; continue; }
		if (/^\S/.test(line)) section = null;
		if (/^\s+nav_translations:\s*$/.test(line)) { inTranslations = true; continue; }
		if (inTranslations) {
			const t = line.match(/^\s{12}(.+?):\s*(.+)$/);
			if (t) { da.set(t[1], t[2]); continue; }
			inTranslations = false;
		}
		if (section !== 'nav') continue;
		if (/^  - [^:]+:\s*$/.test(line)) { order = 0; continue; } // section header
		const labelled = line.match(/^\s+- (?:"([^"]+)"|([^:]+)):\s*(\S+\.md)\s*$/);
		const bare = line.match(/^\s+- (\S+\.md)\s*$/);
		if (labelled) nav.set(labelled[3], { label: labelled[1] ?? labelled[2], order: order++ });
		else if (bare) nav.set(bare[1], { label: null, order: order++ });
	}
	return { nav, da };
}

// ---------- links ----------

// Route for a docs-relative MkDocs path ("guides/coaching.md" -> "/guides/coaching/").
function route(mdPath, lang) {
	const prefix = lang === 'da' ? '/da' : '';
	if (mdPath === 'changelog.md') return `${prefix}/changelog/`;
	if (mdPath === 'whats-new.md') return `${prefix}/whats-new/`;
	let p = mdPath.replace(/\.md$/, '');
	if (p === 'index') return `${prefix}/`;
	p = p.replace(/\/index$/, '');
	return `${prefix}/${p}/`;
}

function rewriteLink(target, file, rel, lang) {
	if (/^(https?:|mailto:|#|\/)/.test(target)) return target;
	const [path, hash] = target.split('#');
	if (!path.endsWith('.md')) {
		warn(file, `relative link not to a page, left as is: ${target}`);
		return target;
	}
	const resolved = posix.normalize(posix.join(posix.dirname(rel), path));
	if (resolved.startsWith('..')) { warn(file, `link outside docs: ${target}`); return target; }
	try { statSync(join(src, resolved)); } catch { warn(file, `link to missing page: ${target}`); }
	return route(resolved, lang) + (hash ? `#${hash}` : '');
}

// ---------- inline transforms (outside code spans) ----------

const KEY_NAMES = { shift: 'Shift', ctrl: 'Ctrl', alt: 'Alt', enter: 'Enter', space: 'Space', tab: 'Tab', esc: 'Esc' };
const keyLabel = (k) => KEY_NAMES[k] ?? (k.length === 1 ? k.toUpperCase() : k[0].toUpperCase() + k.slice(1));

function inline(text, ctx) {
	// Split on code spans so their contents stay untouched.
	return text.split(/(`+[^`]*`+)/).map((part, i) => {
		if (i % 2 === 1) return part;
		let s = part;
		s = s.replace(/:(material|octicons|fontawesome|simple)-[\w-]+:(\{[^}]*\})?\s?/g, '');
		s = s.replace(/\s*\{:?\s*\.[\w-]+(\s+\.[\w-]+)*\s*\}/g, '');
		s = s.replace(/\+\+([\w+-]+?)\+\+/g, (_, keys) => keys.split('+').map((k) => `<kbd>${keyLabel(k)}</kbd>`).join('+'));
		s = s.replace(/\]\(([^)\s]+)\)/g, (_, t) => `](${rewriteLink(t, ctx.file, ctx.rel, ctx.lang)})`);
		s = s.replace(/^(\s*\[[^\]]+\]:\s*)(\S+)/, (_, a, t) => a + rewriteLink(t, ctx.file, ctx.rel, ctx.lang));
		if (/@@(?!MATCHZY_VERSION@@)[A-Z_]+@@/.test(s)) warn(ctx.file, `placeholder left in text: ${s.match(/@@[A-Z_]+@@/)[0]}`);
		return s;
	}).join('');
}

function heading(line, ctx) {
	const m = line.match(/^(#{1,6})\s+(.*?)\s*\{\s*#([\w-]+)\s*\}\s*$/);
	if (!m) return inline(line, ctx);
	const id = ctx.mdx ? `\\{#${m[3]}\\}` : `{#${m[3]}}`;
	return `${m[1]} ${inline(m[2], ctx)} ${id}`;
}

// ---------- block transforms ----------

const ASIDE = { note: 'note', info: 'note', abstract: 'note', tip: 'tip', success: 'tip', question: 'note', warning: 'caution', caution: 'caution', danger: 'danger', failure: 'danger', bug: 'danger', example: 'note', quote: 'note' };

const indentOf = (l) => l.match(/^ */)[0].length;
const dedent = (lines, n) => lines.map((l) => (l.trim() === '' ? '' : l.slice(Math.min(n, indentOf(l)))));

// Lines that belong to an indented block opened at `start` (blank lines or indent >= base + 4).
function takeBlock(lines, start, base) {
	let end = start;
	while (end < lines.length && (lines[end].trim() === '' || indentOf(lines[end]) >= base + 4)) end++;
	while (end > start && lines[end - 1].trim() === '') end--; // trailing blanks stay outside
	return end;
}

function stripOuterBlanks(lines) {
	let a = 0, b = lines.length;
	while (a < b && lines[a].trim() === '') a++;
	while (b > a && lines[b - 1].trim() === '') b--;
	return lines.slice(a, b);
}

const attr = (s) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/[{}]/g, (c) => (c === '{' ? '&#123;' : '&#125;')).replace(/</g, '&lt;');
const plain = (s) => s.replace(/`([^`]*)`/g, '$1').replace(/\*\*([^*]+)\*\*/g, '$1').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').trim();

// <div class="grid cards" markdown> list -> LinkCards. Two shapes are used in the docs:
//   - icon **Title** / --- / description / [icon link text](page.md)
//   - icon **[Title](page.md)** / description
function cards(body, ctx) {
	const items = [];
	let cur = null;
	for (const l of body) {
		if (/^-\s+/.test(l)) { cur = [l.replace(/^-\s+/, '')]; items.push(cur); }
		else if (cur) cur.push(l.replace(/^ {4}/, ''));
	}
	ctx.imports.add('CardGrid').add('LinkCard');
	const out = ['<CardGrid>'];
	for (const item of items) {
		const text = item.join('\n').replace(/:(material|octicons)-[\w-]+:(\{[^}]*\})?\s?/g, '');
		const titleLink = text.match(/\*\*\[([^\]]+)\]\(([^)]+)\)\*\*/);
		const titleBold = text.match(/\*\*([^*]+)\*\*/);
		const links = [...text.matchAll(/\[([^\]]+)\]\(([^)\s]+)\)/g)];
		const title = titleLink ? titleLink[1] : titleBold?.[1];
		const href = titleLink ? titleLink[2] : links.at(-1)?.[2];
		if (!title || !href) { warn(ctx.file, `card without title or link: ${text.slice(0, 60)}`); continue; }
		const desc = text.split('\n').slice(1)
			.filter((l) => l.trim() && l.trim() !== '---' && !(links.length && !titleLink && l.includes(`](${href})`)))
			.map(plain).join(' ');
		out.push(`\t<LinkCard title="${attr(plain(title))}" href="${rewriteLink(href, ctx.file, ctx.rel, ctx.lang)}"${desc ? ` description="${attr(desc)}"` : ''} />`);
	}
	out.push('</CardGrid>');
	return out;
}

// Returns { lines, depth } where depth is the deepest aside nesting inside (for ::: fence length).
function convertBlock(lines, ctx) {
	const res = [];
	let depth = 0;
	let fence = null;
	for (let i = 0; i < lines.length; i++) {
		const line = lines[i];
		const fenceM = line.match(/^\s*(```+|~~~+)/);
		if (fence) {
			res.push(line);
			if (fenceM && fenceM[1][0] === fence[0] && fenceM[1].length >= fence.length && line.trim() === fenceM[1]) fence = null;
			continue;
		}
		if (fenceM) { fence = fenceM[1]; res.push(line); continue; }

		const base = indentOf(line);
		const adm = line.match(/^\s*(!!!|\?\?\?\+?)\s+(\w+)(?:\s+"(.*)")?\s*$/);
		if (adm) {
			const end = takeBlock(lines, i + 1, base);
			const inner = convertBlock(dedent(lines.slice(i + 1, end), base + 4), ctx);
			const pad = ' '.repeat(base);
			const title = adm[3] !== undefined ? inline(adm[3], ctx) : '';
			if (adm[1] === '!!!') {
				const type = ASIDE[adm[2]] ?? (warn(ctx.file, `unknown admonition type ${adm[2]}`), 'note');
				const colons = ':'.repeat(3 + inner.depth);
				res.push(`${pad}${colons}${type}${title ? `[${title}]` : ''}`, ...stripOuterBlanks(inner.lines).map((l) => (l ? pad + l : l)), `${pad}${colons}`);
				depth = Math.max(depth, inner.depth + 1);
			} else {
				res.push(`${pad}<details${adm[1].endsWith('+') ? ' open' : ''}>`, `${pad}<summary>${title || adm[2]}</summary>`, '', ...stripOuterBlanks(inner.lines).map((l) => (l ? pad + l : l)), '', `${pad}</details>`);
				depth = Math.max(depth, inner.depth);
			}
			i = end - 1;
			continue;
		}

		if (/^\s*=== "/.test(line)) {
			ctx.imports.add('Tabs').add('TabItem');
			const pad = ' '.repeat(base);
			res.push(`${pad}<Tabs>`);
			while (i < lines.length && lines[i].startsWith(`${pad}=== "`) && indentOf(lines[i]) === base) {
				const label = lines[i].match(/=== "(.*)"/)[1];
				const end = takeBlock(lines, i + 1, base);
				const inner = convertBlock(dedent(lines.slice(i + 1, end), base + 4), ctx);
				res.push(`${pad}<TabItem label="${attr(label)}">`, '', ...stripOuterBlanks(inner.lines).map((l) => (l ? pad + l : l)), '', `${pad}</TabItem>`);
				depth = Math.max(depth, inner.depth);
				i = end;
				while (i < lines.length && lines[i].trim() === '' && lines[i + 1]?.startsWith(`${pad}=== "`)) i++;
			}
			res.push(`${pad}</Tabs>`);
			i--;
			continue;
		}

		if (/^<div class="grid cards[^"]*" markdown>\s*$/.test(line)) {
			const end = lines.indexOf('</div>', i);
			res.push(...cards(lines.slice(i + 1, end), ctx));
			i = end;
			continue;
		}

		// Tool slots of the old site (settings generator, event explorer, match builder): the
		// Tool component brings the root element, its data and the script.
		if (/^<!--MATCHZY_(SETTINGS|EVENTS)-->$/.test(line.trim())) continue;
		const tool = line.match(/^<div id="mz-\w+" class="mzt-tool mzt-tool--([\w-]+)">/);
		if (tool) {
			let end = i;
			while (lines[end].trim() !== '</div>') end++;
			ctx.tool = true;
			res.push(`<Tool name="${tool[1]}" />`);
			i = end;
			continue;
		}

		if (/^\s*<div\b/.test(line)) warn(ctx.file, `raw <div> left as is: ${line.trim().slice(0, 60)}`);
		if (/^\s*<!--/.test(line) && ctx.mdx) warn(ctx.file, `HTML comment in an .mdx page: ${line.trim()}`);
		res.push(/^#{1,6}\s/.test(line) ? heading(line, ctx) : inline(line, ctx));
	}
	return { lines: res, depth };
}

// ---------- front matter ----------

function splitFrontMatter(text) {
	const m = text.match(/^---\n([\s\S]*?)\n---\n/);
	if (!m) return { meta: {}, body: text };
	const meta = {};
	let key = null;
	for (const l of m[1].split('\n')) {
		const kv = l.match(/^(\w+):\s*(.*)$/);
		if (kv) { key = kv[1]; meta[key] = kv[2] === '' ? [] : kv[2].replace(/^["']|["']$/g, ''); continue; }
		const item = l.match(/^\s+-\s+(.+)$/);
		if (item && key && Array.isArray(meta[key])) meta[key].push(item[1]);
	}
	return { meta, body: text.slice(m[0].length) };
}

const yamlStr = (s) => JSON.stringify(s);

function frontMatter(meta, title, navEntry, lang, nav) {
	const fm = [`title: ${yamlStr(title)}`];
	if (meta.description) fm.push(`description: ${yamlStr(meta.description)}`);
	if (Array.isArray(meta.hide) && meta.hide.includes('toc')) fm.push('tableOfContents: false');
	if (meta.new_in) fm.push(`new_in: ${yamlStr(meta.new_in)}`);
	if (navEntry) {
		fm.push('sidebar:');
		// A section's index page (no nav label) would repeat the group name in the sidebar.
		let label = navEntry.label ?? (lang === 'da' ? 'Oversigt' : 'Overview');
		if (navEntry.label && lang === 'da') label = nav.da.get(label) ?? label;
		if (label && label !== title) fm.push(`  label: ${yamlStr(label)}`);
		fm.push(`  order: ${navEntry.order}`);
	}
	return `---\n${fm.join('\n')}\n---\n`;
}

// ---------- main ----------

function walk(dir) {
	return readdirSync(dir).flatMap((n) => {
		const p = join(dir, n);
		return statSync(p).isDirectory() ? walk(p) : p.endsWith('.md') ? [p] : [];
	});
}

const nav = readNav();
for (const d of OWNED) {
	rmSync(join(out, d), { recursive: true, force: true });
	rmSync(join(out, 'da', d), { recursive: true, force: true });
}

let count = 0;
for (const file of walk(src).sort()) {
	const relDa = relative(src, file).split('\\').join('/');
	if (SKIP.has(relDa)) continue;
	const lang = relDa.endsWith('.da.md') ? 'da' : 'en';
	const rel = relDa.replace(/\.da\.md$/, '.md'); // the English path the page stands for
	const { meta, body } = splitFrontMatter(readFileSync(file, 'utf-8'));
	const ctx = { file: relDa, rel, lang, imports: new Set(), mdx: false };

	const lines = body.split('\n');
	const h1 = lines.findIndex((l) => /^# /.test(l));
	const title = meta.title || (h1 >= 0 ? lines[h1].slice(2).trim() : null);
	if (!title) { warn(relDa, 'no title'); continue; }
	if (h1 >= 0) lines.splice(h1, 1);

	// Pages with tabs or cards need MDX; pass 1 finds out, pass 2 converts with MDX escaping.
	sink = [];
	convertBlock(lines, ctx);
	sink = warnings;
	ctx.mdx = ctx.imports.size > 0 || !!ctx.tool;
	const { lines: converted } = convertBlock(lines, ctx);

	let text = stripOuterBlanks(converted).join('\n').replace(/\n{3,}/g, '\n\n') + '\n';
	const target = join(out, lang === 'da' ? 'da' : '', rel.replace(/\.md$/, ctx.mdx ? '.mdx' : '.md'));
	if (ctx.mdx) {
		const imports = [];
		if (ctx.imports.size) imports.push(`import { ${[...ctx.imports].sort().join(', ')} } from '@astrojs/starlight/components';`);
		if (ctx.tool) imports.push(`import Tool from '${relative(dirname(target), join(site, 'src/components/Tool.astro')).split('\\').join('/')}';`);
		text = `${imports.join('\n')}\n\n${text}`;
	}
	mkdirSync(dirname(target), { recursive: true });
	writeFileSync(target, frontMatter(meta, title, nav.nav.get(rel), lang, nav) + '\n' + text);
	count++;
}

const unique = [...new Set(warnings)];
console.log(`converted ${count} pages into ${relative(site, out)}`);
for (const w of unique) console.warn(`warning: ${w}`);
process.exit(unique.length ? 1 : 0);
