// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import { remarkHeadingId, remarkMermaid, remarkVersion } from './src/plugins/remark-docs.mjs';

// Static site: `npm run build` writes dist/, deploy.sh copies it to the web server.
export default defineConfig({
	site: 'https://matchzy.miksen.me',
	trailingSlash: 'always',
	markdown: { remarkPlugins: [remarkHeadingId, remarkVersion, remarkMermaid] },
	integrations: [
		starlight({
			title: 'MatchZy',
			description: 'Match management, practice mode, coaching, live events and stats for CS2 servers running CounterStrikeSharp.',
			logo: { src: './src/assets/logo.svg', replacesTitle: false },
			favicon: '/favicon.svg',
			defaultLocale: 'root',
			locales: {
				root: { label: 'English', lang: 'en' },
				da: { label: 'Dansk', lang: 'da' },
			},
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/mrc4tt/MatchZy' }],
			editLink: { baseUrl: 'https://github.com/mrc4tt/MatchZy/edit/main/site/' },
			lastUpdated: true,
			// Source engine .cfg files have no grammar; show them as plain text.
			expressiveCode: { shiki: { langAlias: { cfg: 'txt' } } },
			routeMiddleware: './src/routeData.ts',
			components: {
				PageTitle: './src/components/PageTitle.astro',
				MarkdownContent: './src/components/MarkdownContent.astro',
			},
			customCss: ['./src/styles/theme.css'],
			sidebar: [
				{ label: 'Getting started', translations: { da: 'Kom i gang' }, items: [{ autogenerate: { directory: 'getting-started' } }] },
				{ label: 'Guides', translations: { da: 'Guides' }, items: [{ autogenerate: { directory: 'guides' } }] },
				{ label: 'Reference', translations: { da: 'Reference' }, items: [{ autogenerate: { directory: 'reference' } }] },
				{ label: 'Changelog', translations: { da: 'Changelog' }, items: [{ slug: 'whats-new' }, { slug: 'changelog', label: 'Full changelog', translations: { da: 'Fuld changelog' } }] },
			],
		}),
	],
});
