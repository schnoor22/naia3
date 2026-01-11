<script lang="ts">
	import { toasts, type Toast } from '$lib/stores/signalr';

	const iconByType: Record<Toast['type'], string> = {
		success: '✓',
		error: '✕',
		warning: '⚠',
		info: 'ℹ'
	};

	const colorByType: Record<Toast['type'], string> = {
		success: 'bg-emerald-500',
		error: 'bg-red-500',
		warning: 'bg-amber-500',
		info: 'bg-naia-500'
	};
</script>

<div class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 pointer-events-none">
	{#each $toasts as toast (toast.id)}
		<div 
			class="flex items-start gap-3 p-4 bg-white dark:bg-gray-800 rounded-lg shadow-lg border border-gray-200 dark:border-gray-700 min-w-[300px] max-w-[400px] pointer-events-auto animate-fadeIn"
		>
			<div class="flex-shrink-0 w-6 h-6 {colorByType[toast.type]} rounded-full flex items-center justify-center text-white text-xs font-bold">
				{iconByType[toast.type]}
			</div>
			<div class="flex-1 min-w-0">
				<p class="font-medium text-gray-900 dark:text-gray-100">{toast.title}</p>
				{#if toast.message}
					<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">{toast.message}</p>
				{/if}
			</div>
			<button 
				onclick={() => toasts.remove(toast.id)}
				class="flex-shrink-0 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200"
			>
				<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
					<path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
				</svg>
			</button>
		</div>
	{/each}
</div>
