import { defineCollection } from 'astro:content';
import { z } from 'astro/zod';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

export const collections = {
	docs: defineCollection({ loader: docsLoader(), schema: docsSchema({
		// Version a page was added in (badge while recent, like the old site).
		extend: z.object({ new_in: z.string().optional() }),
	}) }),
};
