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
					
				{:else}
					
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
						
					</div>
					<div>
						<h3 class="font-semibold text-gray-900 dark:text-gray-100 flex items-center gap-1.5">
							QuestDB Console
							
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
					
					

					<!-- Kafka -->
					<div class="flex items-center gap-2 px-4 py-2 bg-orange-50 dark:bg-orange-900/20 rounded-lg border border-orange-200 dark:border-orange-800">
						<span>📡</span>
						<span class="font-medium">Kafka</span>
						{#if metrics?.isRunning}
							<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
						{/if}
					</div>

					

					<!-- Ingestion Worker -->
					<div class="flex items-center gap-2 px-4 py-2 bg-purple-50 dark:bg-purple-900/20 rounded-lg border border-purple-200 dark:border-purple-800">
						<span>⚙️</span>
						<span class="font-medium">Enrichment</span>
						{#if metrics?.isRunning}
							<span class="inline-block w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
						{/if}
					</div>

					

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
