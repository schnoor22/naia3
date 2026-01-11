import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	preprocess: vitePreprocess(),
	kit: {
		// Static adapter outputs to 'build' folder
		// Copy contents to Naia.Api/wwwroot for embedded deployment
		adapter: adapter({
			pages: 'build',
			assets: 'build',
			fallback: 'index.html', // SPA mode - all routes go to index.html
			precompress: false,
			strict: true
		}),
		paths: {
			// Base path - empty for root deployment
			base: ''
		}
	}
};

export default config;
