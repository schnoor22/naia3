<script lang="ts">
	import { onMount } from 'svelte';
	import { searchPoints, getDataSources, getCurrentValue, type Point, type DataSource } from '$lib/services/api';
	import QuickTrend from '$lib/components/QuickTrend.svelte';

	let points = $state<Point[]>([]);
	let dataSources = $state<DataSource[]>([]);
	let totalPoints = $state(0);
	let loading = $state(true);
	let error = $state<string | null>(null);

	// Search/filter state
	let searchQuery = $state('');
	let selectedDataSource = $state('');
	let showEnabledOnly = $state(false);
	let currentPage = $state(0);
	const pageSize = 25;

	// Current values cache
	let currentValues = $state<Record<string, { value: any; timestamp: string } | null>>({});

	// Selected point for quick trend
	let selectedPoint = $state<Point | null>(null);

	async function loadPoints() {
		loading = true;
		error = null;
		try {
			const result = await searchPoints({
				tagName: searchQuery || undefined,
				dataSourceId: selectedDataSource || undefined,
				enabled: showEnabledOnly ? true : undefined,
				skip: currentPage * pageSize,
				take: pageSize
			});
			points = result.data;
			totalPoints = result.total;

			// Load current values for visible points
			loadCurrentValues();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load points';
		} finally {
			loading = false;
		}
	}

	async function loadDataSources() {
		try {
			dataSources = await getDataSources();
		} catch (e) {
			console.error('Failed to load data sources:', e);
		}
	}

	async function loadCurrentValues() {
		for (const point of points) {
			if (point.pointSequenceId) {
				try {
					const value = await getCurrentValue(point.id);
					currentValues[point.id] = { value: value.value, timestamp: value.timestamp };
				} catch {
					currentValues[point.id] = null;
				}
			}
		}
		// Trigger reactivity
		currentValues = { ...currentValues };
	}

	function handleSearch() {
		currentPage = 0;
		loadPoints();
	}

	function nextPage() {
		if ((currentPage + 1) * pageSize < totalPoints) {
			currentPage++;
			loadPoints();
		}
	}

	function prevPage() {
		if (currentPage > 0) {
			currentPage--;
			loadPoints();
		}
	}

	function formatValue(val: any): string {
		if (val === null || val === undefined) return '—';
		if (typeof val === 'number') return val.toFixed(2);
		return String(val);
	}

	function formatTimestamp(ts: string): string {
		return new Date(ts).toLocaleTimeString();
	}

	onMount(() => {
		loadDataSources();
		loadPoints();
	});
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">Point Browser</h1>
			<p class="text-gray-500 dark:text-gray-400">Search and manage historian tags</p>
		</div>
		<span class="text-sm text-gray-500">{totalPoints.toLocaleString()} points total</span>
	</div>

	<!-- Filters -->
	<div class="card p-4">
		<div class="flex flex-wrap gap-4">
			<div class="flex-1 min-w-[200px]">
				<label for="search" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
					Search Tags
				</label>
				<div class="relative">
					<input
						type="text"
						id="search"
						class="input pl-10"
						placeholder="Filter by tag name..."
						bind:value={searchQuery}
						onkeydown={(e) => e.key === 'Enter' && handleSearch()}
					/>
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400">
						<path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
					</svg>
				</div>
			</div>

			<div class="w-48">
				<label for="datasource" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
					Data Source
				</label>
				<select
					id="datasource"
					class="input"
					bind:value={selectedDataSource}
					onchange={handleSearch}
				>
					<option value="">All Sources</option>
					{#each dataSources as source}
						<option value={source.id}>{source.name}</option>
					{/each}
				</select>
			</div>

			<div class="flex items-end">
				<label class="flex items-center gap-2 cursor-pointer">
					<input
						type="checkbox"
						class="w-4 h-4 rounded border-gray-300 text-naia-500 focus:ring-naia-500"
						bind:checked={showEnabledOnly}
						onchange={handleSearch}
					/>
					<span class="text-sm text-gray-700 dark:text-gray-300">Enabled only</span>
				</label>
			</div>

			<div class="flex items-end">
				<button class="btn btn-primary" onclick={handleSearch}>
					Search
				</button>
			</div>
		</div>
	</div>

	{#if error}
		<div class="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-red-700 dark:text-red-400">
			{error}
		</div>
	{/if}

	<!-- Results Table -->
	<div class="card overflow-hidden">
		<div class="table-container">
			<table class="table">
				<thead>
					<tr>
						<th>Tag Name</th>
						<th>Description</th>
						<th>Current Value</th>
						<th>Units</th>
						<th>Data Type</th>
						<th>Status</th>
						<th class="text-right">Actions</th>
					</tr>
				</thead>
				<tbody>
					{#if loading}
						{#each Array(5) as _}
							<tr>
								<td><div class="skeleton h-4 w-48"></div></td>
								<td><div class="skeleton h-4 w-32"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td><div class="skeleton h-4 w-12"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td></td>
							</tr>
						{/each}
					{:else if points.length === 0}
						<tr>
							<td colspan="7" class="text-center py-8 text-gray-500">
								No points found matching your criteria
							</td>
						</tr>
					{:else}
						{#each points as point}
							<tr class="group">
								<td>
									<div class="font-mono text-sm font-medium text-gray-900 dark:text-gray-100">
										{point.name}
									</div>
									{#if point.sourceAddress && point.sourceAddress !== point.name}
										<div class="text-xs text-gray-400">{point.sourceAddress}</div>
									{/if}
								</td>
								<td class="max-w-[200px] truncate" title={point.description}>
									{point.description || '—'}
								</td>
								<td>
									{#if currentValues[point.id]}
										<div class="font-mono tabular-nums">
											{formatValue(currentValues[point.id]?.value)}
										</div>
										<div class="text-xs text-gray-400">
											{formatTimestamp(currentValues[point.id]?.timestamp ?? '')}
										</div>
									{:else if point.pointSequenceId}
										<span class="text-gray-400">Loading...</span>
									{:else}
										<span class="text-gray-400">Not synced</span>
									{/if}
								</td>
								<td>{point.engineeringUnits || '—'}</td>
								<td>
									<span class="badge badge-neutral">{point.dataType}</span>
								</td>
								<td>
									{#if point.enabled}
										<span class="badge badge-success">Enabled</span>
									{:else}
										<span class="badge badge-neutral">Disabled</span>
									{/if}
								</td>
								<td class="text-right">
									<button
										class="btn btn-ghost btn-sm opacity-0 group-hover:opacity-100 transition-opacity"
										onclick={() => selectedPoint = point}
										disabled={!point.pointSequenceId}
										title={point.pointSequenceId ? 'View trend' : 'Point not synced to time-series'}
									>
										<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
											<path stroke-linecap="round" stroke-linejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z" />
										</svg>
										Trend
									</button>
								</td>
							</tr>
						{/each}
					{/if}
				</tbody>
			</table>
		</div>

		<!-- Pagination -->
		<div class="flex items-center justify-between px-4 py-3 border-t border-gray-200 dark:border-gray-800">
			<div class="text-sm text-gray-500">
				Showing {currentPage * pageSize + 1} to {Math.min((currentPage + 1) * pageSize, totalPoints)} of {totalPoints.toLocaleString()}
			</div>
			<div class="flex gap-2">
				<button 
					class="btn btn-secondary btn-sm"
					onclick={prevPage}
					disabled={currentPage === 0}
				>
					Previous
				</button>
				<button 
					class="btn btn-secondary btn-sm"
					onclick={nextPage}
					disabled={(currentPage + 1) * pageSize >= totalPoints}
				>
					Next
				</button>
			</div>
		</div>
	</div>
</div>

<!-- Quick Trend Modal -->
{#if selectedPoint}
	<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onclick={() => selectedPoint = null}>
		<div class="bg-white dark:bg-gray-900 rounded-xl shadow-2xl w-full max-w-4xl m-4 max-h-[90vh] overflow-hidden" onclick={(e) => e.stopPropagation()}>
			<div class="flex items-center justify-between p-4 border-b border-gray-200 dark:border-gray-800">
				<div>
					<h3 class="font-semibold text-gray-900 dark:text-gray-100">{selectedPoint.name}</h3>
					<p class="text-sm text-gray-500">{selectedPoint.description || 'No description'}</p>
				</div>
				<button 
					class="p-2 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg"
					onclick={() => selectedPoint = null}
				>
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
						<path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
					</svg>
				</button>
			</div>
			<div class="p-4">
				<QuickTrend pointId={selectedPoint.id} pointName={selectedPoint.name} units={selectedPoint.engineeringUnits} />
			</div>
		</div>
	</div>
{/if}
