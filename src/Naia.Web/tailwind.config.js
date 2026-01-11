/** @type {import('tailwindcss').Config} */
export default {
	content: ['./src/**/*.{html,js,svelte,ts}'],
	darkMode: 'class',
	theme: {
		extend: {
			colors: {
				// NAIA brand colors - inspired by the dolphin logo
				naia: {
					50: '#f0fdfa',
					100: '#ccfbf1',
					200: '#99f6e4',
					300: '#5eead4',
					400: '#2dd4bf',
					500: '#14b8a6', // Primary teal
					600: '#0d9488',
					700: '#0f766e',
					800: '#115e59',
					900: '#134e4a',
					950: '#042f2e',
				},
				// Status colors
				status: {
					healthy: '#10b981',
					degraded: '#f59e0b',
					unhealthy: '#ef4444',
					unknown: '#6b7280',
				}
			},
			fontFamily: {
				sans: ['Inter', 'system-ui', 'sans-serif'],
				mono: ['JetBrains Mono', 'Consolas', 'monospace'],
			},
			animation: {
				'pulse-slow': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite',
				'spin-slow': 'spin 2s linear infinite',
			},
			boxShadow: {
				'glow': '0 0 20px rgba(20, 184, 166, 0.3)',
				'glow-lg': '0 0 40px rgba(20, 184, 166, 0.4)',
			}
		},
	},
	plugins: [],
}
