import { writable, derived } from 'svelte/store';

// SignalR connection state
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export const connectionState = writable<ConnectionState>('disconnected');
export const pendingCount = writable<number>(0);

// Derived store for connection status display
export const connectionStatus = derived(connectionState, ($state) => {
	switch ($state) {
		case 'connected':
			return { text: 'Connected', color: 'text-emerald-500', dot: 'bg-emerald-500' };
		case 'connecting':
			return { text: 'Connecting...', color: 'text-amber-500', dot: 'bg-amber-500' };
		case 'reconnecting':
			return { text: 'Reconnecting...', color: 'text-amber-500', dot: 'bg-amber-500' };
		default:
			return { text: 'Disconnected', color: 'text-gray-400', dot: 'bg-gray-400' };
	}
});

// Toast notifications store
export interface Toast {
	id: string;
	type: 'info' | 'success' | 'warning' | 'error';
	title: string;
	message?: string;
	duration?: number;
}

function createToastStore() {
	const { subscribe, update } = writable<Toast[]>([]);

	return {
		subscribe,
		add: (toast: Omit<Toast, 'id'>) => {
			const id = crypto.randomUUID();
			update((toasts) => [...toasts, { ...toast, id }]);
			
			// Auto remove after duration
			const duration = toast.duration ?? 5000;
			if (duration > 0) {
				setTimeout(() => {
					update((toasts) => toasts.filter((t) => t.id !== id));
				}, duration);
			}
			
			return id;
		},
		remove: (id: string) => {
			update((toasts) => toasts.filter((t) => t.id !== id));
		},
		clear: () => {
			update(() => []);
		}
	};
}

export const toasts = createToastStore();
