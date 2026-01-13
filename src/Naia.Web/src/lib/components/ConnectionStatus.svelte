<script lang="ts">
	import { connectionStatus } from '$lib/stores/signalr';
	import { getHealth } from '$lib/services/api';
	import { onMount } from 'svelte';

	let apiHealthy = $state(false);

	onMount(async () => {
		try {
			const health = await getHealth();
			apiHealthy = health?.status === 'healthy';
		} catch (e) {
			apiHealthy = false;
		}
		
		// Check every 10 seconds
		const interval = setInterval(async () => {
			try {
				const health = await getHealth();
				apiHealthy = health?.status === 'healthy';
			} catch (e) {
				apiHealthy = false;
			}
		}, 10000);
		
		return () => clearInterval(interval);
	});

	// Show API health status instead of SignalR status
	const displayStatus = $derived({
		connected: apiHealthy ? { text: 'Connected', color: 'text-emerald-500', dot: 'bg-emerald-500' } : { text: 'Disconnected', color: 'text-gray-400', dot: 'bg-gray-400' },
		signalr: $connectionStatus
	});
</script>

<div class="flex items-center gap-2 text-sm">
	<span class="status-dot {displayStatus.connected.dot}"></span>
	<span class="{displayStatus.connected.color}">{displayStatus.connected.text}</span>
</div>

<style>
	.status-dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		animation: pulse 2s infinite;
	}

	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50% { opacity: 0.5; }
	}
</style>
