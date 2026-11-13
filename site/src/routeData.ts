// Starlight route middleware: the "New" badge in the sidebar for pages whose new_in is recent.
import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { getCollection } from 'astro:content';
import { isRecent } from './data/plugin';

let recent: Set<string> | null = null;

export const onRequest = defineRouteMiddleware(async (context) => {
	recent ??= new Set((await getCollection('docs')).filter((e) => isRecent(e.data.new_in)).map((e) => e.id.replace(/^da\//, '')));
	const { locale, sidebar } = context.locals.starlightRoute;
	const badge = { text: locale === 'da' ? 'Ny' : 'New', variant: 'default' as const };
	const walk = (entries: typeof sidebar) => {
		for (const e of entries) {
			if (e.type === 'group') walk(e.entries);
			else if (!e.badge && recent!.has(e.href.replace(/^\/(da\/)?|\/$/g, ''))) e.badge = badge;
		}
	};
	walk(sidebar);
});
