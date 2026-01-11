<script lang="ts">
	interface Props {
		status: 'healthy' | 'degraded' | 'unhealthy' | 'unknown';
		title: string;
		subtitle?: string;
		value?: string | number;
		icon?: string;
		loading?: boolean;
	}

	let { status, title, subtitle, value, icon, loading = false }: Props = $props();

	const statusColors: Record<string, string> = {
		healthy: 'border-emerald-500/50 bg-emerald-500/5',
		degraded: 'border-amber-500/50 bg-amber-500/5',
		unhealthy: 'border-red-500/50 bg-red-500/5',
		unknown: 'border-gray-500/50 bg-gray-500/5',
	};

	const dotColors: Record<string, string> = {
		healthy: 'bg-emerald-500',
		degraded: 'bg-amber-500',
		unhealthy: 'bg-red-500',
		unknown: 'bg-gray-500',
	};
</script>

<div class="card border-l-4 {statusColors[status]} overflow-hidden">
	<div class="p-5">
		{#if loading}
			<div class="animate-pulse space-y-3">
				<div class="h-4 w-24 bg-gray-200 dark:bg-gray-700 rounded"></div>
				<div class="h-8 w-16 bg-gray-200 dark:bg-gray-700 rounded"></div>
			</div>
		{:else}
			<div class="flex items-start justify-between">
				<div>
					<div class="flex items-center gap-2">
						<span class="status-dot {dotColors[status]}"></span>
						<h3 class="text-sm font-medium text-gray-500 dark:text-gray-400">{title}</h3>
					</div>
					{#if value !== undefined}
						<p class="mt-2 text-2xl font-bold text-gray-900 dark:text-gray-100 tabular-nums">
							{value}
						</p>
					{/if}
					{#if subtitle}
						<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">{subtitle}</p>
					{/if}
				</div>
				{#if icon}
					<div class="text-2xl opacity-50">{icon}</div>
				{/if}
			</div>
		{/if}
	</div>
</div>
