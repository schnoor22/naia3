<script lang="ts">
	import { onMount } from 'svelte';
	import { browser } from '$app/environment';
	import { searchPoints, getDataSources, getCurrentValue, getHistory, type Point, type DataSource, type HistoricalDataResponse } from '$lib/services/api';

	// Dynamically import Plotly only in browser (it uses 'self' which doesn't exist in SSR)
	let Plotly: typeof import('plotly.js-dist-min') | null = null;

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

	// Quick Trend modal state
	let selectedPoint = $state<Point | null>(null);
	let trendLoading = $state(false);
	let trendError = $state<string | null>(null);
	let trendData = $state<HistoricalDataResponse | null>(null);
	let chartContainer = $state<HTMLElement | undefined>(undefined);
	
	const timeRanges = [
		{ label: '1H', hours: 1 },
		{ label: '8H', hours: 8 },
		{ label: '24H', hours: 24 },
		{ label: '7D', hours: 168 },
		{ label: 'All', hours: null }, // All available data
	];
	let selectedTimeRange = $state(timeRanges[3]); // Default 7D to capture replay data

	async function loadTrendData() {
		if (!selectedPoint) return;
		
		trendLoading = true;
		trendError = null;
		
		try {
			let end = new Date();
			let start: Date;
			
			// For "All" range, use a very wide window
			if (selectedTimeRange.hours === null) {
				start = new Date('2020-01-01'); // Far past
				end = new Date('2030-01-01');   // Far future
			} else {
				start = new Date(end.getTime() - selectedTimeRange.hours * 60 * 60 * 1000);
			}
			
			console.log(`Loading trend for ${selectedPoint.name}: ${start.toISOString()} to ${end.toISOString()}`);
			trendData = await getHistory(selectedPoint.id, start, end, 5000);
			console.log('Trend data loaded:', trendData?.data?.length, 'points');
		} catch (e) {
			trendError = e instanceof Error ? e.message : 'Failed to load trend data';
			console.error('Trend load error:', e);
		} finally {
			trendLoading = false;
		}
	}

	function openTrend(point: Point) {
		selectedPoint = point;
		trendData = null;
		trendError = null;
		loadTrendData();
	}

	function closeTrend() {
		selectedPoint = null;
		trendData = null;
	}

	function renderChart(container: HTMLElement, data: HistoricalDataResponse) {
		if (!Plotly) {
			console.warn('Plotly not loaded yet');
			container.innerHTML = '<div style="padding: 20px; text-align: center; color: #999;">Loading chart library...</div>';
			return;
		}

		if (!data.data || data.data.length === 0) {
			console.warn('No data to render');
			container.innerHTML = '<div style="padding: 20px; text-align: center; color: #999;">No data available</div>';
			return;
		}

		console.log(`Rendering Plotly chart with ${data.data.length} points`);

		// Clear container
		container.innerHTML = '';

		try {
			// Convert data to Plotly format
			const timestamps = data.data.map(d => {
				const ts = d.timestamp.includes('Z') ? d.timestamp : d.timestamp + 'Z';
				return new Date(ts);
			});
			const values = data.data.map(d => d.value);

			// Sort by timestamp
			const combined = timestamps.map((t, i) => ({ t, v: values[i] }));
			combined.sort((a, b) => a.t.getTime() - b.t.getTime());

			const trace = {
				x: combined.map(c => c.t),
				y: combined.map(c => c.v),
				type: 'scatter' as const,
				mode: 'lines' as const,
				fill: 'tozeroy' as const,
				fillcolor: 'rgba(20, 184, 166, 0.2)',
				line: {
					color: 'rgb(20, 184, 166)',
					width: 2
				},
				hovertemplate: '%{x|%Y-%m-%d %H:%M}<br>Value: %{y:.2f}<extra></extra>'
			};

			const layout = {
				paper_bgcolor: '#1f2937',
				plot_bgcolor: '#1f2937',
				margin: { l: 50, r: 20, t: 20, b: 50 },
				height: 300,
				xaxis: {
					type: 'date' as const,
					gridcolor: '#374151',
					tickfont: { color: '#9ca3af' },
					linecolor: '#374151'
				},
				yaxis: {
					gridcolor: '#374151',
					tickfont: { color: '#9ca3af' },
					linecolor: '#374151',
					title: {
						text: data.tagName || 'Value',
						font: { color: '#9ca3af', size: 12 }
					}
				},
				hovermode: 'x unified' as const,
				showlegend: false
			};

			const config = {
				responsive: true,
				displayModeBar: true,
				modeBarButtonsToRemove: ['lasso2d', 'select2d'] as const,
				displaylogo: false
			};

			Plotly.newPlot(container, [trace], layout, config);
			console.log('Plotly chart rendered successfully!');

			// Return cleanup function
			return () => {
				console.log('Cleaning up Plotly chart');
				if (Plotly) Plotly.purge(container);
			};
		} catch (e) {
			console.error('Error creating/rendering chart:', e);
			container.innerHTML = `<div style="padding: 20px; text-align: center; color: #f87171;">Error rendering chart: ${e instanceof Error ? e.message : String(e)}</div>`;
			return;
		}
	}

	function formatTrendValue(val: number): string {
		if (Math.abs(val) >= 1000) return val.toFixed(0);
		if (Math.abs(val) >= 1) return val.toFixed(2);
		return val.toFixed(4);
	}

	// Svelte action to render chart
	function chartEffect(node: HTMLElement, data: HistoricalDataResponse | null) {
		let cleanup: (() => void) | undefined;
		
		if (data && data.data && data.data.length > 0) {
			console.log('chartEffect: Rendering chart with data');
			cleanup = renderChart(node, data);
		} else {
			console.log('chartEffect: No data to render');
		}
		
		return {
			update(newData: HistoricalDataResponse | null) {
				// Cleanup old chart
				if (cleanup) {
					cleanup();
					cleanup = undefined;
				}
				
				// Render new chart
				if (newData && newData.data && newData.data.length > 0) {
					console.log('chartEffect update: Rendering chart with new data');
					cleanup = renderChart(node, newData);
				}
			},
			destroy() {
				if (cleanup) {
					cleanup();
				}
			}
		};
	}

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

	onMount(async () => {
		// Dynamically import Plotly only in browser
		if (browser) {
			const plotlyModule = await import('plotly.js-dist-min');
			Plotly = plotlyModule.default;
		}
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
					<span class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400">
						<Icon name="search" size="20" />
					</span>
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
						<th>Data Source</th>
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
								<td><div class="skeleton h-4 w-20"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td><div class="skeleton h-4 w-12"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td><div class="skeleton h-4 w-16"></div></td>
								<td></td>
							</tr>
						{/each}
					{:else if points && Array.isArray(points) && points.length === 0}
						<tr>
							<td colspan="8" class="text-center py-8 text-gray-500">
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
									<span class="text-sm">{point.dataSourceName || '—'}</span>
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
									<span class="badge badge-neutral">{point.valueType}</span>
								</td>
								<td>
									{#if point.isEnabled}
										<span class="badge badge-success">Enabled</span>
									{:else}
										<span class="badge badge-neutral">Disabled</span>
									{/if}
								</td>
								<td class="text-right">
									<button
										class="btn btn-ghost btn-sm opacity-0 group-hover:opacity-100 transition-opacity"
										onclick={() => openTrend(point)}
										title="View trend"
									>
										<Icon name="trends" size="16" />
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
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm" onclick={closeTrend} role="dialog" aria-modal="true" aria-labelledby="trend-modal-title">
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<!-- svelte-ignore a11y_click_events_have_key_events -->
		<div class="bg-white dark:bg-gray-900 rounded-xl shadow-2xl w-full max-w-3xl m-4 overflow-hidden" onclick={(e) => e.stopPropagation()}>
			<!-- Header -->
			<div class="flex items-center justify-between p-4 border-b border-gray-200 dark:border-gray-700">
				<div>
					<h3 id="trend-modal-title" class="font-semibold text-lg text-gray-900 dark:text-gray-100">{selectedPoint.name}</h3>
					<p class="text-sm text-gray-500">{selectedPoint.description || 'No description'}</p>
				</div>
				<button 
					class="p-2 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg transition-colors"
					onclick={closeTrend}
					aria-label="Close trend modal"
				>
					<Icon name="close" size="20" />
				</button>
			</div>
			
			<!-- Time Range Selector -->
			<div class="px-4 py-3 border-b border-gray-200 dark:border-gray-700 flex items-center gap-4">
				<span class="text-sm text-gray-500">Time Range:</span>
				<div class="flex gap-1 bg-gray-100 dark:bg-gray-800 rounded-lg p-1">
					{#each timeRanges as range}
						<button
							class="px-3 py-1 text-sm rounded-md transition-colors"
							class:bg-teal-500={selectedTimeRange.label === range.label}
							class:text-white={selectedTimeRange.label === range.label}
							class:text-gray-600={selectedTimeRange.label !== range.label}
							class:dark:text-gray-400={selectedTimeRange.label !== range.label}
							onclick={() => { selectedTimeRange = range; loadTrendData(); }}
						>
							{range.label}
						</button>
					{/each}
				</div>
			</div>
			
			<!-- Chart Area -->
			<div class="p-4">
				{#if trendLoading}
					<div class="h-64 flex items-center justify-center">
						<div class="flex flex-col items-center gap-2 text-gray-400">
							<Icon name="spinner" size="32" class="text-naia-500" />
							<span>Loading trend data...</span>
						</div>
					</div>
				{:else if trendError}
					<div class="h-64 flex items-center justify-center">
						<div class="text-center">
							<div class="text-red-400 mb-2">
								<Icon name="warning" size="48" class="mx-auto" />
							</div>
							<p class="text-red-400 font-medium">{trendError}</p>
							<button class="mt-2 text-sm text-teal-500 hover:underline" onclick={loadTrendData}>Retry</button>
						</div>
					</div>
				{:else if trendData && trendData.data && trendData.data.length > 0}
					<div class="space-y-4">
						<!-- Stats -->
						<div class="grid grid-cols-4 gap-4 text-center">
							<div class="bg-gray-50 dark:bg-gray-800 rounded-lg p-3">
								<div class="text-xs text-gray-500 uppercase">Points</div>
								<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{trendData.data.length.toLocaleString()}</div>
							</div>
							<div class="bg-gray-50 dark:bg-gray-800 rounded-lg p-3">
								<div class="text-xs text-gray-500 uppercase">Min</div>
								<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatTrendValue(Math.min(...trendData.data.map(d => d.value)))}</div>
							</div>
							<div class="bg-gray-50 dark:bg-gray-800 rounded-lg p-3">
								<div class="text-xs text-gray-500 uppercase">Max</div>
								<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatTrendValue(Math.max(...trendData.data.map(d => d.value)))}</div>
							</div>
							<div class="bg-gray-50 dark:bg-gray-800 rounded-lg p-3">
								<div class="text-xs text-gray-500 uppercase">Latest</div>
								<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatTrendValue(trendData.data[trendData.data.length - 1].value)}</div>
							</div>
						</div>
						
						<!-- Lightweight Charts -->
						<div class="bg-gray-900 rounded-lg overflow-hidden" bind:this={chartContainer} use:chartEffect={trendData}></div>
					</div>
				{:else}
					<div class="h-64 flex items-center justify-center">
						<div class="text-center text-gray-400">
							<Icon name="trends" size="48" class="mx-auto mb-2 opacity-50" />
							<p>No data available for this time range</p>
							<p class="text-sm mt-1">Try selecting a different time range or check if data is being collected</p>
						</div>
					</div>
				{/if}
			</div>
		</div>
	</div>
{/if}
