// Remark plugins for the docs pages (the syntax scripts/convert-mkdocs.mjs leaves in them).
import { visit } from 'unist-util-visit';
import { releasedVersion } from '../data/version.mjs';

// "## Heading {#custom-id}" keeps the id the old site used (links like ready-system#tournaments).
export function remarkHeadingId() {
	return (tree) => {
		visit(tree, 'heading', (node) => {
			const last = node.children.at(-1);
			if (last?.type !== 'text') return;
			const m = last.value.match(/\s*\{#([\w-]+)\}\s*$/);
			if (!m) return;
			last.value = last.value.slice(0, m.index);
			node.data ??= {};
			node.data.hProperties = { ...node.data.hProperties, id: m[1] };
		});
	};
}

// @@MATCHZY_VERSION@@ in text, code and link URLs -> the newest release (src/data/version.mjs).
export function remarkVersion() {
	// The newest release, so download links in the docs point at a zip that exists.
	const version = releasedVersion();
	const sub = (s) => s.replaceAll('@@MATCHZY_VERSION@@', version);
	return (tree) => {
		visit(tree, (node) => {
			if (typeof node.value === 'string' && node.value.includes('@@')) node.value = sub(node.value);
			if (typeof node.url === 'string' && node.url.includes('@@')) node.url = sub(node.url);
		});
	};
}

// ```mermaid blocks -> <pre class="mermaid">source</pre>, drawn in the browser by
// src/components/MarkdownContent.astro (Expressive Code would otherwise show them as code).
export function remarkMermaid() {
	return (tree) => {
		visit(tree, 'code', (node, index, parent) => {
			if (node.lang !== 'mermaid' || !parent) return;
			parent.children[index] = {
				type: 'mermaidBlock',
				data: { hName: 'pre', hProperties: { className: ['mermaid', 'not-content'] } },
				children: [{ type: 'text', value: node.value }],
			};
		});
	};
}
