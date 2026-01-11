import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [sveltekit()],
	server: {
		port: 5173,
		proxy: {
			// Proxy API calls to .NET backend during development
			'/api': {
				target: 'http://localhost:5052',
				changeOrigin: true
			},
			'/health': {
				target: 'http://localhost:5052',
				changeOrigin: true
			},
			'/hangfire': {
				target: 'http://localhost:5052',
				changeOrigin: true
			},
			// SignalR WebSocket proxy
			'/hubs': {
				target: 'http://localhost:5052',
				changeOrigin: true,
				ws: true
			}
		}
	}
});
