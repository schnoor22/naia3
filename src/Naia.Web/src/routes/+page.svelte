<script lang="ts">
	import { onMount } from 'svelte';
	import StatusCard from '$lib/components/StatusCard.svelte';
	import MetricCard from '$lib/components/MetricCard.svelte';
	import Toast from '$lib/components/Toast.svelte';
	import { getHealth, getPipelineMetrics, getIngestionStatus, getSuggestionStats, type HealthStatus, type PipelineMetrics, type SuggestionStats } from '$lib/services/api';
	import { pendingCount } from '$lib/stores/signalr';

	let health = $state<HealthStatus | null>(null);
	let metrics = $state<PipelineMetrics | null>(null);
	let suggestionStats = $state<SuggestionStats | null>(null);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let lastRefresh = $state<Date>(new Date());
	let isInitialLoad = $state(true);

	async function loadData() {
		try {
			const [healthData, metricsData, statsData] = await Promise.all([
				getHealth().catch(() => null),
				getPipelineMetrics().catch(() => null),
				getSuggestionStats().catch(() => null)
			]);

			// Always update - no caching to ensure live updates
			health = healthData;
			metrics = metricsData;
			suggestionStats = statsData;
			
			if (statsData) {
				pendingCount.set(statsData.pending);
			}
			
			lastRefresh = new Date();
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load data';
		} finally {
			loading = false;
			isInitialLoad = false;
		}
	}

	onMount(() => {
		loadData();
		// Auto-refresh every 10 seconds
		const interval = setInterval(loadData, 10000);
		return () => clearInterval(interval);
	});

	function getServiceStatus(serviceName: string): 'healthy' | 'degraded' | 'unhealthy' | 'unknown' {
		if (!health?.checks) return 'unknown';
		const check = health.checks[serviceName];
		if (!check) return 'unknown';
		return check.status === 'healthy' ? 'healthy' : 'unhealthy';
	}

	function formatNumber(n: number): string {
		if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
		if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K';
		return n.toString();
	}

	function formatRate(n: number): string {
		return n.toFixed(1) + '/s';
	}
</script>

<Toast />

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">Dashboard</h1>
			<p class="text-gray-500 dark:text-gray-400">NAIA Industrial Historian Command Center</p>
		</div>
		<div class="flex items-center gap-4">
			<span class="text-sm text-gray-400">
				Last updated: {lastRefresh.toLocaleTimeString()}
			</span>
			<button onclick={loadData} class="btn btn-secondary btn-sm" disabled={loading}>
				{#if loading}
					<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
						<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
						<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
					</svg>
				{:else}
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
						<path stroke-linecap="round" stroke-linejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
					</svg>
				{/if}
				Refresh
			</button>
		</div>
	</div>

	{#if error}
		<div class="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-red-700 dark:text-red-400">
			{error}
		</div>
	{/if}

	<!-- System Health Section -->
	<section>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">System Health</h2>
		<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
			<StatusCard 
				status={getServiceStatus('postgresql')}
				title="PostgreSQL"
				subtitle="Metadata & Config"
				icon="🐘"
				loading={isInitialLoad}
			/>
			<StatusCard 
				status={getServiceStatus('questdb')}
				title="QuestDB"
				subtitle="Time-Series Storage"
				icon="📊"
				loading={isInitialLoad}
			/>
			<StatusCard 
				status={getServiceStatus('redis')}
				title="Redis"
				subtitle="Current Value Cache"
				icon="⚡"
				loading={isInitialLoad}
			/>
			<StatusCard 
				status={metrics?.isRunning ? 'healthy' : 'degraded'}
				title="Kafka Pipeline"
				subtitle={metrics?.isRunning ? 'Streaming' : 'Stopped'}
				icon="📡"
				loading={isInitialLoad}
			/>
		</div>
	</section>

	<!-- Ingestion Metrics -->
	<section>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">Ingestion Metrics</h2>
		<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
			<MetricCard 
				title="Points/Second"
				value={metrics ? formatRate(metrics.pointsPerSecond) : '—'}
				icon="📈"
				loading={isInitialLoad}
			/>
			<MetricCard 
				title="Total Ingested"
				value={metrics ? formatNumber(metrics.totalPointsIngested) : '—'}
				icon="📦"
				loading={isInitialLoad}
			/>
			<MetricCard 
				title="Batches Processed"
				value={metrics ? formatNumber(metrics.batchesProcessed) : '—'}
				icon="✅"
				loading={isInitialLoad}
			/>
			<MetricCard 
				title="Errors"
				value={metrics?.errors ?? '—'}
				icon="⚠️"
				loading={isInitialLoad}
			/>
		</div>
	</section>

	<!-- Pattern Learning Section -->
	<section>
		<div class="flex items-center justify-between mb-4">
			<h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100">Pattern Learning</h2>
			<a href="/patterns" class="text-naia-500 hover:text-naia-600 text-sm font-medium">
				View all suggestions →
			</a>
		</div>
		<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
			<div class="card p-5 border-l-4 border-naia-500">
				<p class="text-sm font-medium text-gray-500 dark:text-gray-400">Pending Review</p>
				<p class="mt-2 text-3xl font-bold text-naia-600 dark:text-naia-400 tabular-nums">
					{suggestionStats?.pending ?? $pendingCount}
				</p>
				<p class="mt-1 text-xs text-gray-500">Suggestions awaiting approval</p>
			</div>
			<MetricCard 
				title="Approved Today"
				value={suggestionStats?.approvedToday ?? '—'}
				icon="👍"
				loading={isInitialLoad}
			/>
			<MetricCard 
				title="Rejected Today"
				value={suggestionStats?.rejectedToday ?? '—'}
				icon="👎"
				loading={isInitialLoad}
			/>
			<MetricCard 
				title="Avg Confidence"
				value={suggestionStats ? `${Math.round((suggestionStats.averageConfidence ?? 0) * 100)}%` : '—'}
				icon="🎯"
				loading={isInitialLoad}
			/>
		</div>
	</section>

	<!-- Quick Actions -->
	<section>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">Quick Actions</h2>
		<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
			<a href="/points" class="card p-5 hover:shadow-lg transition-shadow group">
				<div class="flex items-center gap-4">
					<div class="p-3 bg-blue-500/10 rounded-lg group-hover:bg-blue-500/20 transition-colors">
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-6 h-6 text-blue-500">
							<path stroke-linecap="round" stroke-linejoin="round" d="M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 010 3.75H5.625a1.875 1.875 0 010-3.75z" />
						</svg>
					</div>
					<div>
						<h3 class="font-semibold text-gray-900 dark:text-gray-100">Browse Points</h3>
						<p class="text-sm text-gray-500 dark:text-gray-400">Search and manage historian tags</p>
					</div>
				</div>
			</a>

			<a href="/trends" class="card p-5 hover:shadow-lg transition-shadow group">
				<div class="flex items-center gap-4">
					<div class="p-3 bg-emerald-500/10 rounded-lg group-hover:bg-emerald-500/20 transition-colors">
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-6 h-6 text-emerald-500">
							<path stroke-linecap="round" stroke-linejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z" />
						</svg>
					</div>
					<div>
						<h3 class="font-semibold text-gray-900 dark:text-gray-100">View Trends</h3>
						<p class="text-sm text-gray-500 dark:text-gray-400">Chart historical time-series data</p>
					</div>
				</div>
			</a>

			<a href="/patterns" class="card p-5 hover:shadow-lg transition-shadow group">
				<div class="flex items-center gap-4">
					<div class="p-3 bg-purple-500/10 rounded-lg group-hover:bg-purple-500/20 transition-colors">
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-6 h-6 text-purple-500">
							<path stroke-linecap="round" stroke-linejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456zM16.894 20.567L16.5 21.75l-.394-1.183a2.25 2.25 0 00-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 001.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 001.423 1.423l1.183.394-1.183.394a2.25 2.25 0 00-1.423 1.423z" />
						</svg>
					</div>
					<div class="flex-1">
						<div class="flex items-center gap-2">
							<h3 class="font-semibold text-gray-900 dark:text-gray-100">Review Patterns</h3>
							{#if $pendingCount > 0}
								<span class="bg-naia-500 text-white text-xs font-bold px-2 py-0.5 rounded-full">
									{$pendingCount}
								</span>
							{/if}
						</div>
						<p class="text-sm text-gray-500 dark:text-gray-400">Approve AI pattern suggestions</p>
					</div>
				</div>
			</a>

			<a href="http://app.naia.run:9000" target="_blank" rel="noopener noreferrer" class="card p-5 hover:shadow-lg transition-shadow group">
				<div class="flex items-center gap-4">
					<div class="p-3 bg-orange-500/10 rounded-lg group-hover:bg-orange-500/20 transition-colors">
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-6 h-6 text-orange-500">
							<path stroke-linecap="round" stroke-linejoin="round" d="M20.25 6.375c0 2.278-3.694 4.125-8.25 4.125S3.75 8.653 3.75 6.375m16.5 0c0-2.278-3.694-4.125-8.25-4.125S3.75 4.097 3.75 6.375m16.5 0v11.25c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125V6.375m16.5 0v3.75m-16.5-3.75v3.75m16.5 0v3.75C20.25 16.153 16.556 18 12 18s-8.25-1.847-8.25-4.125v-3.75m16.5 0c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125" />
						</svg>
					</div>
					<div>
						<h3 class="font-semibold text-gray-900 dark:text-gray-100 flex items-center gap-1.5">
							QuestDB Console
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3 h-3">
								<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
							</svg>
						</h3>
						<p class="text-sm text-gray-500 dark:text-gray-400">Query raw time-series data</p>
					</div>
				</div>
			</a>
		</div>
	</section>

	<!-- Architecture Overview -->
	<section class="card">
		<div class="card-header">
			<h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100">Pipeline Architecture</h2>
			<p class="text-xs text-gray-500 dark:text-gray-400 mt-1">Real-time data processing with AI-powered pattern learning</p>
		</div>
		<div class="card-body">
			<div class="space-y-6">
				<!-- Main Pipeline Flow -->
				<div class="flex flex-wrap items-center justify-center gap-4 text-sm">
					<!-- Industrial Data Sources -->
					<div class="flex flex-col gap-2">
						<div class="text-xs text-gray-500 dark:text-gray-400 text-center font-semibold">Industrial Sources</div>
						<div class="flex flex-wrap gap-2">
							<div class="flex items-center gap-2 px-3 py-1.5 bg-blue-50 dark:bg-blue-900/20 rounded border border-blue-200 dark:border-blue-800 text-xs">
								<span>🏭</span>
								<span class="font-medium">OSIsoft PI</span>
								{#if health?.checks?.postgresql?.status === 'healthy'}
									<span class="inline-block w-1.5 h-1.5 bg-green-500 rounded-full"></span>
								{/if}
							</div>
							<div class="flex items-center gap-2 px-3 py-1.5 bg-indigo-50 dark:bg-indigo-900/20 rounded border border-indigo-200 dark:border-indigo-800 text-xs">
								<span>⚙️</span>
								<span class="font-medium">OPC UA</span>
							</div>
							<div class="flex items-center gap-2 px-3 py-1.5 bg-cyan-50 dark:bg-cyan-900/20 rounded border border-cyan-200 dark:border-cyan-800 text-xs">
								<span>📡</span>
								<span class="font-medium">Modbus</span>
							</div>
							<div class="flex items-center gap-2 px-3 py-1.5 bg-teal-50 dark:bg-teal-900/20 rounded border border-teal-200 dark:border-teal-800 text-xs">
								<span>📄</span>
								<span class="font-medium">CSV/Flat Files</span>
							</div>
						</div>
					</div>
					
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5 text-gray-400">
						<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
					</svg>

					<!-- Kafka -->
					<div class="flex items-center gap-2 px-4 py-2 bg-orange-50 dark:bg-orange-900/20 rounded-lg border border-orange-200 dark:border-orange-800">
						<span>📡</span>
						<span class="font-medium">Kafka</span>
						{#if metrics?.isRunning}
							<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
						{/if}
					</div>

					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5 text-gray-400">
						<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
					</svg>

					<!-- Ingestion Worker -->
					<div class="flex items-center gap-2 px-4 py-2 bg-purple-50 dark:bg-purple-900/20 rounded-lg border border-purple-200 dark:border-purple-800">
						<span>⚙️</span>
						<span class="font-medium">Enrichment</span>
						{#if metrics?.isRunning}
							<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
						{/if}
					</div>

					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5 text-gray-400">
						<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
					</svg>

					<!-- Storage -->
					<div class="flex flex-col gap-2">
						<div class="flex items-center gap-2 px-4 py-2 bg-emerald-50 dark:bg-emerald-900/20 rounded-lg border border-emerald-200 dark:border-emerald-800">
							<span>📊</span>
							<span class="font-medium">QuestDB</span>
							{#if health?.checks?.questdb?.status === 'healthy'}
								<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
							{/if}
						</div>
						<div class="flex items-center gap-2 px-4 py-2 bg-red-50 dark:bg-red-900/20 rounded-lg border border-red-200 dark:border-red-800">
							<span>⚡</span>
							<span class="font-medium">Redis Cache</span>
							{#if health?.checks?.redis?.status === 'healthy'}
								<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
							{/if}
						</div>
					</div>
				</div>

				<!-- Pattern Engine with Data Flow -->
				<div class="relative flex items-center justify-center gap-8 py-8">
					<!-- Input: Time-Series Data -->
					<div class="flex flex-col gap-2 items-center">
						<div class="px-4 py-2 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700 text-xs">
							<div class="font-semibold text-gray-700 dark:text-gray-300">Input Data</div>
							<div class="text-gray-500 dark:text-gray-400">Time-series streams</div>
						</div>
					</div>

					<!-- Arrow to Engine -->
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-6 h-6 text-naia-500">
						<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
					</svg>

					<!-- Center Pattern Engine -->
					<div class="relative">
						<div class="flex flex-col items-center gap-3 px-6 py-4 bg-gradient-to-br from-naia-50 to-naia-100 dark:from-naia-900/50 dark:to-naia-800/30 rounded-xl border-2 border-naia-400 dark:border-naia-600 shadow-lg">
							<span class="text-3xl">🧠</span>
							<div class="text-center">
								<div class="font-bold text-naia-700 dark:text-naia-300 text-sm">Pattern Engine</div>
								<div class="text-xs text-naia-600 dark:text-naia-400 mt-1 space-y-0.5">
									<div>• Behavioral Analysis</div>
									<div>• Correlation Detection</div>
									<div>• Pattern Matching</div>
									<div>• Confidence Scoring</div>
								</div>
							</div>
						</div>
					</div>

					<!-- Arrow from Engine -->
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-6 h-6 text-naia-500">
						<path stroke-linecap="round" stroke-linejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
					</svg>

					<!-- Output: Suggestions -->
					<div class="flex flex-col gap-2 items-center">
						<div class="px-4 py-2 bg-purple-50 dark:bg-purple-900/20 rounded-lg border border-purple-200 dark:border-purple-800 text-xs">
							<div class="font-semibold text-purple-700 dark:text-purple-300">AI Suggestions</div>
							<div class="text-purple-600 dark:text-purple-400">Equipment patterns</div>
						</div>
					</div>
				</div>

				<!-- Feedback Loop -->
				<div class="bg-naia-50 dark:bg-naia-900/20 rounded-lg p-4 border border-naia-200 dark:border-naia-700">
					<div class="flex items-center justify-center gap-6 text-xs">
						<div class="text-center">
							<div class="font-bold text-emerald-700 dark:text-emerald-400">✓ Approve</div>
							<div class="text-gray-600 dark:text-gray-400">Confidence +5%</div>
						</div>
						<div class="text-2xl text-naia-500">↺</div>
						<div class="text-center">
							<div class="font-bold text-naia-700 dark:text-naia-400">Better Models</div>
							<div class="text-gray-600 dark:text-gray-400">Learns from feedback</div>
						</div>
						<div class="text-2xl text-naia-500">↺</div>
						<div class="text-center">
							<div class="font-bold text-purple-700 dark:text-purple-400">Improved Suggestions</div>
							<div class="text-gray-600 dark:text-gray-400">Higher accuracy</div>
						</div>
					</div>
				</div>
			</div>
		</div>
	</section>
</div>
