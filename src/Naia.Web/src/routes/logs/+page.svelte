<script lang="ts">
	import { onMount } from 'svelte';
	
	interface LogEntry {
		timestamp: string;
		level: string;
		source: string;
		message: string;
		exception: string | null;
		properties: string | null;
	}
	
	let logs = $state<LogEntry[]>([]);
	let total = $state(0);
	let loading = $state(true);
	let error = $state<string | null>(null);
	
	// Filters
	let levelFilter = $state('All');
	let sourceFilter = $state('All');
	let searchQuery = $state('');
	let timeRange = $state(60); // minutes
	let autoRefresh = $state(true);
	let skip = $state(0);
	let take = $state(100);
	
	let intervalId: number | undefined;
	let lastFetchedData: string | null = null;
	
	async function loadLogs(skipLoadingIndicator = false) {
		if (!skipLoadingIndicator) loading = true;
		try {
			const params = new URLSearchParams();
			const levelVal = levelFilter?.toLowerCase?.() ?? '';
			const sourceVal = sourceFilter?.toLowerCase?.() ?? '';
			
			if (levelVal && levelVal !== 'all') params.set('level', levelVal);
			if (sourceVal && sourceVal !== 'all') params.set('source', sourceVal);
			if (searchQuery && searchQuery.length > 0) params.set('search', searchQuery);
			params.set('minutes', timeRange.toString());
			params.set('skip', skip.toString());
			params.set('take', take.toString());
			
			const response = await fetch(`/api/logs?${params}`);
			if (!response.ok) throw new Error(`HTTP ${response.status}`);
			
			const text = await response.text();
			// Only update if data has changed to avoid flashing
			if (text !== lastFetchedData) {
				const data = JSON.parse(text);
				logs = data.logs || [];
				total = data.total || 0;
				lastFetchedData = text;
			}
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load logs';
			console.error('Logs error:', error);
		} finally {
			if (!skipLoadingIndicator) loading = false;
		}
	}
	
	function getLevelColor(level: string): string {
		switch (level?.toLowerCase()) {
			case 'debug': return 'text-gray-500 dark:text-gray-400';
			case 'information': return 'text-blue-600 dark:text-blue-400';
			case 'warning': return 'text-yellow-600 dark:text-yellow-400';
			case 'error': return 'text-red-600 dark:text-red-400';
			case 'fatal': return 'text-red-700 dark:text-red-500 font-bold';
			default: return 'text-gray-600 dark:text-gray-300';
		}
	}
	
	function getLevelBadge(level: string): string {
		switch (level?.toLowerCase()) {
			case 'debug': return 'bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300';
			case 'information': return 'bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300';
			case 'warning': return 'bg-yellow-100 dark:bg-yellow-900 text-yellow-700 dark:text-yellow-300';
			case 'error': return 'bg-red-100 dark:bg-red-900 text-red-700 dark:text-red-300';
			case 'fatal': return 'bg-red-200 dark:bg-red-800 text-red-900 dark:text-red-200 font-bold';
			default: return 'bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300';
		}
	}
	
	function formatTimestamp(timestamp: string): string {
		// Parse the UTC timestamp and display in user's local timezone
		const date = new Date(timestamp);
		// Use toLocaleString with explicit options for consistent formatting
		return date.toLocaleString(undefined, {
			year: 'numeric',
			month: '2-digit',
			day: '2-digit',
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit',
			hour12: false
		});
	}
	
	function getShortSource(source: string | null): string {
		if (!source) return 'Unknown';
		const parts = source.split('.');
		return parts[parts.length - 1] || source;
	}
	
	onMount(() => {
		loadLogs();
		
		if (autoRefresh) {
			intervalId = setInterval(() => loadLogs(true), 5000) as unknown as number;
		}
		
		return () => {
			if (intervalId) clearInterval(intervalId);
		};
	});
	
	function handleFilterChange() {
		skip = 0;
		lastFetchedData = null; // Reset cache when filters change
		loadLogs();
	}
	
	function toggleAutoRefresh() {
		// @ts-ignore - Svelte 5 state proxy comparison
		autoRefresh = !autoRefresh;
		
		if (autoRefresh) {
			intervalId = setInterval(() => loadLogs(true), 5000) as unknown as number;
		} else if (intervalId) {
			clearInterval(intervalId);
		}
	}
	
	function previousPage() {
		if (skip > 0) {
			skip = Math.max(0, skip - take);
			loadLogs();
		}
	}
	
	function nextPage() {
		if (skip + take < total) {
			skip += take;
			loadLogs();
		}
	}
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">System Logs</h1>
			<p class="text-gray-500 dark:text-gray-400">Real-time structured logging from all services</p>
		</div>
		<div class="flex items-center gap-2">
			<button 
				onclick={toggleAutoRefresh}
				class="btn btn-sm {autoRefresh ? 'btn-primary' : 'btn-secondary'}"
			>
				{autoRefresh ? '⏸' : '▶'} Auto-refresh
			</button>
			<button onclick={loadLogs} class="btn btn-sm btn-secondary" disabled={loading}>
				{loading ? 'Loading...' : 'Refresh'}
			</button>
		</div>
	</div>
	
	<!-- Filters -->
	<div class="card p-4">
		<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
			<div>
				<label class="block text-sm font-medium mb-2">Level</label>
				<select bind:value={levelFilter} onchange={handleFilterChange} class="input">
					<option>All</option>
					<option>Debug</option>
					<option>Information</option>
					<option>Warning</option>
					<option>Error</option>
					<option>Fatal</option>
				</select>
			</div>
			
			<div>
				<label class="block text-sm font-medium mb-2">Source</label>
				<select bind:value={sourceFilter} onchange={handleFilterChange} class="input">
					<option>All</option>
					<option>Naia.Api</option>
					<option>Naia.Ingestion</option>
					<option>Naia.Infrastructure</option>
					<option>Naia.PatternEngine</option>
					<option>Naia.Connectors</option>
				</select>
			</div>
			
			<div>
				<label class="block text-sm font-medium mb-2">Time Range</label>
				<select bind:value={timeRange} onchange={handleFilterChange} class="input">
					<option value={15}>Last 15 min</option>
					<option value={60}>Last hour</option>
					<option value={240}>Last 4 hours</option>
					<option value={1440}>Last 24 hours</option>
					<option value={10080}>Last 7 days</option>
				</select>
			</div>
			
			<div>
				<label class="block text-sm font-medium mb-2">Search</label>
				<input 
					type="text" 
					bind:value={searchQuery} 
					onkeyup={(e) => e.key === 'Enter' && handleFilterChange()}
					placeholder="Search logs..." 
					class="input"
				/>
			</div>
		</div>
	</div>
	
	{#if error}
		<div class="alert alert-error">
			{error}
		</div>
	{/if}
	
	<!-- Logs Table -->
	<div class="card overflow-hidden">
		<div class="overflow-x-auto">
			<table class="table">
				<thead>
					<tr>
						<th class="w-40">Timestamp</th>
						<th class="w-24">Level</th>
						<th class="w-32">Source</th>
						<th>Message</th>
					</tr>
				</thead>
				<tbody>
					{#if loading}
						<tr>
							<td colspan="4" class="text-center py-8 text-gray-500">
								Loading logs...
							</td>
						</tr>
					{:else if logs.length === 0}
						<tr>
							<td colspan="4" class="text-center py-8 text-gray-500">
								No logs found
							</td>
						</tr>
					{:else}
					{#each logs as log (log.timestamp + log.message)}
							<tr class="hover:bg-gray-50 dark:hover:bg-gray-800/50">
								<td class="text-sm text-gray-500 dark:text-gray-400 font-mono">
									{formatTimestamp(log.timestamp)}
								</td>
								<td>
									<span class="px-2 py-1 text-xs font-medium rounded {getLevelBadge(log.level)}">
										{log.level}
									</span>
								</td>
								<td class="text-sm text-gray-600 dark:text-gray-400 font-mono">
									{getShortSource(log.source)}
								</td>
								<td>
									<div class="text-sm {getLevelColor(log.level)} font-mono">
										{log.message}
									</div>
									{#if log.exception}
										<details class="mt-2">
											<summary class="cursor-pointer text-xs text-red-600 dark:text-red-400 hover:underline">
												View Exception
											</summary>
											<pre class="mt-2 p-2 bg-red-50 dark:bg-red-900/20 rounded text-xs overflow-x-auto">{log.exception}</pre>
										</details>
									{/if}
								</td>
							</tr>
						{/each}
					{/if}
				</tbody>
			</table>
		</div>
		
		<!-- Pagination -->
		<div class="flex items-center justify-between p-4 border-t border-gray-200 dark:border-gray-700">
			<div class="text-sm text-gray-500 dark:text-gray-400">
				Showing {skip + 1} to {Math.min(skip + take, total)} of {total} logs
			</div>
			<div class="flex gap-2">
				<button 
					onclick={previousPage} 
					disabled={skip === 0}
					class="btn btn-sm btn-secondary"
				>
					Previous
				</button>
				<button 
					onclick={nextPage} 
					disabled={skip + take >= total}
					class="btn btn-sm btn-secondary"
				>
					Next
				</button>
			</div>
		</div>
	</div>
</div>
