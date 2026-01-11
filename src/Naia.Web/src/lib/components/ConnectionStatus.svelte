<script lang="ts">
	import { connectionStatus } from '$lib/stores/signalr';
</script>

<div class="flex items-center gap-2 text-sm">
	<span class="status-dot {$connectionStatus.dot}"></span>
	<span class="{$connectionStatus.color}">{$connectionStatus.text}</span>
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
