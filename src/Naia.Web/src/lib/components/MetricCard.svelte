<script lang="ts">
	interface Props {
		title: string;
		value: string | number;
		change?: number;
		changeLabel?: string;
		icon?: string;
		loading?: boolean;
	}

	let { title, value, change, changeLabel, icon, loading = false }: Props = $props();
</script>

<div class="card overflow-hidden">
	<div class="p-5">
		{#if loading}
			<div class="animate-pulse space-y-3">
				<div class="h-4 w-24 bg-gray-200 dark:bg-gray-700 rounded"></div>
				<div class="h-8 w-20 bg-gray-200 dark:bg-gray-700 rounded"></div>
			</div>
		{:else}
			<div class="flex items-start justify-between">
				<div>
					<p class="text-sm font-medium text-gray-500 dark:text-gray-400">{title}</p>
					<p class="mt-2 text-3xl font-bold text-gray-900 dark:text-gray-100 tabular-nums">
						{value}
					</p>
					{#if change !== undefined}
						<div class="mt-2 flex items-center gap-1 text-sm">
							{#if change > 0}
								<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4 text-emerald-500">
									<path stroke-linecap="round" stroke-linejoin="round" d="M4.5 15.75l7.5-7.5 7.5 7.5" />
								</svg>
								<span class="text-emerald-600 dark:text-emerald-400">+{change}%</span>
							{:else if change < 0}
								<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4 text-red-500">
									<path stroke-linecap="round" stroke-linejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
								</svg>
								<span class="text-red-600 dark:text-red-400">{change}%</span>
							{:else}
								<span class="text-gray-500">0%</span>
							{/if}
							{#if changeLabel}
								<span class="text-gray-400 dark:text-gray-500">{changeLabel}</span>
							{/if}
						</div>
					{/if}
				</div>
				{#if icon}
					<div class="p-3 bg-naia-500/10 rounded-lg">
						<span class="text-2xl">{icon}</span>
					</div>
				{/if}
			</div>
		{/if}
	</div>
</div>
