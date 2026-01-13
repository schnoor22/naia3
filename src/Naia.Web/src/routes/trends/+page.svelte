<script lang="ts">
	import { onMount, tick } from 'svelte';
	import { browser } from '$app/environment';
	import { page } from '$app/stores';
	import { searchPoints, getPoint, getHistory, type Point, type HistoricalDataResponse } from '$lib/services/api';

	// Dynamically import Plotly only in browser
	let Plotly: typeof import('plotly.js-dist-min') | null = null;

	let chartElement: HTMLDivElement | null = null;
	let loading = $state(false);
	let searchLoading = $state(false);
	let showDataTable = $state(true);

	// Trend data storage for table display
	let trendData = $state<Array<{ pointName: string; timestamp: Date; value: number; color: string }>>([]);

	// Point selection
	let searchQuery = $state('');
	let searchResults = $state<Point[]>([]);
	let selectedPoints = $state<Point[]>([]);
	let showSearch = $state(false);
	let searchTimeout: ReturnType<typeof setTimeout> | null = null;

	// Time range
	const timeRanges = [
		{ label: '1H', hours: 1 },
		{ label: '8H', hours: 8 },
		{ label: '24H', hours: 24 },
		{ label: '7D', hours: 168 },
		{ label: '30D', hours: 720 },
	];
	let selectedRange = $state(timeRanges[2]); // Default 24h

	// Colors for multiple series
	const seriesColors = [
		'#14b8a6', // teal (naia)
		'#3b82f6', // blue
		'#f59e0b', // amber
		'#ef4444', // red
		'#8b5cf6', // purple
		'#10b981', // emerald
		'#f97316', // orange
		'#ec4899', // pink
	];

	async function loadAllTags() {
		searchLoading = true;
		try {
			const result = await searchPoints({ tagName: '', take: 500 });
			searchResults = result.data.filter(p => !selectedPoints.find(s => s.id === p.id));
		} catch (e) {
			console.error('Failed to load tags:', e);
			searchResults = [];
		} finally {
			searchLoading = false;
		}
	}

	async function searchTags() {
		const query = searchQuery.trim();
		
		// If empty, show all tags
		if (!query) {
			loadAllTags();
			return;
		}

		if (searchTimeout) clearTimeout(searchTimeout);

		searchTimeout = setTimeout(async () => {
			searchLoading = true;
			try {
				const result = await searchPoints({ tagName: query === '*' ? '' : query, take: 500 });
				searchResults = result.data.filter(p => !selectedPoints.find(s => s.id === p.id));
			} catch (e) {
				console.error('Search failed:', e);
				searchResults = [];
			} finally {
				searchLoading = false;
			}
		}, 200);
	}

	function openSearch() {
		showSearch = true;
		searchQuery = '';
		// Auto-load all tags when opening the dropdown
		loadAllTags();
	}

	function closeSearch() {
		showSearch = false;
		searchQuery = '';
		searchResults = [];
	}

	function addPoint(point: Point) {
		if (selectedPoints && Array.isArray(selectedPoints) && selectedPoints.length >= 8) return;
		selectedPoints = [...(selectedPoints || []), point];
		closeSearch();
		// Load chart data - loadChartData will wait for element to be ready
		loadChartData();
	}

	function removePoint(point: Point) {
		selectedPoints = (selectedPoints || []).filter(p => p.id !== point.id);
		if (!selectedPoints || !Array.isArray(selectedPoints) || selectedPoints.length === 0) {
			trendData = [];
			if (chartElement && Plotly) {
				Plotly.purge(chartElement);
			}
		} else {
			loadChartData();
		}
	}

	function changeTimeRange(range: typeof timeRanges[0]) {
		selectedRange = range;
		if (selectedPoints && Array.isArray(selectedPoints) && selectedPoints.length > 0) {
			loadChartData();
		}
	}

	async function loadChartData() {
		if (!selectedPoints || !Array.isArray(selectedPoints) || selectedPoints.length === 0) {
			trendData = [];
			if (chartElement && Plotly) {
				Plotly.purge(chartElement);
			}
			return;
		}

		// Prevent concurrent loads
		if (loading) {
			console.log('Chart load already in progress, skipping');
			return;
		}

		try {
			// Unset loading first to make sure element will be rendered
			loading = false;
			await tick();

			// Wait for chart element to be available (with generous timeout)
			let attempts = 0;
			while (!chartElement && attempts < 50) {
				console.log(`Waiting for chartElement, attempt ${attempts}`, { chartElement });
				await tick();
				attempts++;
			}

			console.log('After wait loop:', { chartElement, attempts, hasParent: chartElement?.parentElement });

			if (!chartElement) {
				console.warn('Chart element failed to initialize after 50 ticks');
				return;
			}

			// Store element reference before async operations
			const element = chartElement;

			// Fetch data WITHOUT setting loading = true (don't hide the element)
			const end = new Date();
			const start = new Date(end.getTime() - selectedRange.hours * 60 * 60 * 1000);

			const dataPromises = selectedPoints.map(point =>
				getHistory(point.id, start, end, 2000).catch(() => null)
			);

			const results = await Promise.all(dataPromises);
			
			// Wait for browser to paint and calculate layout
			await new Promise(resolve => {
				requestAnimationFrame(() => {
					requestAnimationFrame(resolve);
				});
			});
			
			console.log('Before renderChart, element in DOM:', {
				offsetWidth: element.offsetWidth,
				offsetHeight: element.offsetHeight,
				parent: element.parentElement?.className
			});
			
			renderChart(results, selectedPoints, start, end, element);
		} catch (e) {
			console.error('Failed to load chart data:', e);
		} finally {
			loading = false;
		}
	}

	function renderChart(dataResults: (HistoricalDataResponse | null)[], points: Point[], rangeStart: Date, rangeEnd: Date, element: HTMLDivElement) {
		if (!Plotly) {
			console.warn('Plotly not loaded yet');
			return;
		}
		
		if (!element) {
			console.warn('Chart element not available in renderChart');
			return;
		}

		const traces: any[] = [];
		const allTrendData: Array<{ pointName: string; timestamp: Date; value: number; color: string }> = [];

		dataResults.forEach((data, index) => {
			if (!data || !data.data || data.data.length === 0) return;

			const point = points[index];
			const color = seriesColors && Array.isArray(seriesColors) ? seriesColors[index % seriesColors.length] : '#000000';

			const timestamps = data.data.map(d => {
				const ts = d.timestamp.includes('Z') ? d.timestamp : d.timestamp + 'Z';
				return new Date(ts);
			});
			const values = data.data.map(d => d.value);

			const combined = timestamps.map((t, i) => ({ t, v: values[i] }));
			combined.sort((a, b) => a.t.getTime() - b.t.getTime());

			combined.forEach(item => {
				allTrendData.push({
					pointName: point.name,
					timestamp: item.t,
					value: item.v,
					color: color
				});
			});

			traces.push({
				x: combined.map(c => c.t),
				y: combined.map(c => c.v),
				type: 'scatter',
				mode: 'lines',
				name: point.name,
				line: { color, width: 2 },
				hovertemplate: '%{x|%b %d, %H:%M:%S}<br><b>%{fullData.name}</b>: %{y:,.4f}<extra></extra>'
			});
		});

		allTrendData.sort((a, b) => b.timestamp.getTime() - a.timestamp.getTime());
		trendData = allTrendData;

		if (traces.length === 0) {
			console.warn('No traces to render');
			trendData = [];
			return;
		}

		console.log('Rendering chart with', traces.length, 'traces');

		// Determine tick format based on time range
		let tickformat = '%H:%M';
		
		if (selectedRange.hours <= 1) {
			tickformat = '%H:%M:%S';
		} else if (selectedRange.hours <= 24) {
			tickformat = '%H:%M';
		} else if (selectedRange.hours <= 168) {
			tickformat = '%b %d %H:%M';
		} else {
			tickformat = '%b %d';
		}

		const layout = {
			paper_bgcolor: 'transparent',
			plot_bgcolor: '#1f2937',
			margin: { l: 70, r: 30, t: 20, b: 60 },
			height: 450,
			hovermode: 'x unified' as const,
			xaxis: {
				type: 'date' as const,
				range: [rangeStart, rangeEnd],
				gridcolor: '#374151',
				tickfont: { color: '#9ca3af', size: 11 },
				tickformat: tickformat,
				nticks: 10,
				linecolor: '#4b5563',
				linewidth: 1,
				showgrid: true,
				zeroline: false,
				tickangle: -30
			},
			yaxis: {
				gridcolor: '#374151',
				tickfont: { color: '#9ca3af', size: 11 },
				linecolor: '#4b5563',
				linewidth: 1,
				showgrid: true,
				zeroline: false,
				tickformat: ',.0f'
			},
			legend: {
				orientation: 'h' as const,
				x: 0,
				y: 1.12,
				bgcolor: 'transparent',
				font: { color: '#d1d5db', size: 11 }
			},
			font: { family: 'system-ui, -apple-system, sans-serif', color: '#f3f4f6' }
		};

		const config = {
			responsive: true,
			displayModeBar: true,
			modeBarButtonsToRemove: ['lasso2d', 'select2d', 'autoScale2d'] as const,
			displaylogo: false,
			scrollZoom: true
		};

		try {
			console.log('Chart element dimensions:', {
				offsetWidth: element.offsetWidth,
				offsetHeight: element.offsetHeight,
				clientWidth: element.clientWidth,
				clientHeight: element.clientHeight,
				parent: element.parentElement?.classList.value
			});
			Plotly.newPlot(element, traces, layout as any, config);
			console.log('Chart rendered successfully');
		} catch (error) {
			console.error('Failed to render chart - full error:', error);
			if (error instanceof Error) {
				console.error('Error message:', error.message);
				console.error('Error stack:', error.stack);
			}
		}
	}

	function formatTimestamp(date: Date): string {
		return date.toLocaleString('en-US', {
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit',
			hour12: false
		});
	}

	function formatValue(value: number): string {
		if (Math.abs(value) >= 1000000) {
			return (value / 1000000).toFixed(2) + 'M';
		} else if (Math.abs(value) >= 1000) {
			return (value / 1000).toFixed(2) + 'k';
		} else if (Math.abs(value) < 0.01 && value !== 0) {
			return value.toExponential(2);
		}
		return value.toFixed(4);
	}

	onMount(async () => {
		if (browser) {
			const plotlyModule = await import('plotly.js-dist-min');
			Plotly = plotlyModule.default;
		}

		const pointId = $page.url.searchParams.get('pointId');
		
		if (pointId) {
			try {
				const point = await getPoint(pointId);
				if (point) {
					selectedPoints = [point];
					loadChartData();
				}
			} catch (e) {
				console.error('Failed to load point from query params:', e);
			}
		}

		return () => {
			if (Plotly && chartElement) {
				Plotly.purge(chartElement);
			}
		};
	});
</script>

<div class="space-y-4">
	<!-- Header Bar -->
	<div class="card">
		<div class="p-4 flex flex-wrap items-center gap-4">
			<!-- Tag Selection Area -->
			<div class="flex-1 min-w-[200px]">
				<div class="flex flex-wrap items-center gap-2">
					{#each selectedPoints as point, index}
						<span 
							class="inline-flex items-center gap-1.5 pl-3 pr-2 py-1.5 rounded-lg text-sm font-medium text-white shadow-sm"
							style="background-color: {seriesColors[index % seriesColors.length]}"
						>
							<span class="max-w-[200px] truncate">{point.name}</span>
							<button 
								class="hover:bg-white/20 rounded p-0.5 transition-colors"
								onclick={() => removePoint(point)}
								title="Remove"
							>
								
							</button>
						</span>
					{/each}
					
					{#if selectedPoints.length < 8}
						<div class="relative">
							<button 
								class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium border-2 border-dashed transition-all
									{showSearch ? 'border-naia-500 text-naia-500 bg-naia-500/10' : 'border-gray-400 dark:border-gray-600 text-gray-500 hover:border-naia-500 hover:text-naia-500'}"
								onclick={() => showSearch ? closeSearch() : openSearch()}
							>
								
								Add Tag
								<span class="text-xs opacity-60">({selectedPoints.length}/8)</span>
							</button>

							{#if showSearch}
								<div class="absolute top-full left-0 mt-2 w-[450px] bg-white dark:bg-gray-800 rounded-xl shadow-2xl border border-gray-200 dark:border-gray-700 z-20 overflow-hidden">
									<div class="p-3 border-b border-gray-200 dark:border-gray-700">
										<input
											type="text"
											class="input w-full"
											placeholder="Search or filter tags..."
											bind:value={searchQuery}
											oninput={searchTags}
										/>
									</div>
									<div class="max-h-80 overflow-y-auto">
										{#if searchLoading}
											<div class="p-6 text-center text-gray-500">
												
												Loading tags...
											</div>
										{:else if searchResults.length > 0}
											<div class="text-xs text-gray-400 px-4 py-2 bg-gray-50 dark:bg-gray-700/30 border-b border-gray-100 dark:border-gray-700/50">
												{searchResults.length} tag{searchResults.length !== 1 ? 's' : ''} found
											</div>
											{#each searchResults as result}
												<button
													class="w-full px-4 py-3 text-left hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors border-b border-gray-100 dark:border-gray-700/50 last:border-0"
													onclick={() => addPoint(result)}
												>
													<div class="font-mono text-sm font-medium text-gray-900 dark:text-gray-100">{result.name}</div>
													{#if result.description}
														<div class="text-xs text-gray-500 truncate mt-0.5">{result.description}</div>
													{/if}
													{#if result.dataSourceName}
														<div class="text-xs text-naia-400 mt-0.5">{result.dataSourceName}</div>
													{/if}
												</button>
											{/each}
										{:else}
											<div class="p-6 text-center text-gray-500">
												
												{#if searchQuery}
													No tags found for "{searchQuery}"
												{:else}
													No tags available
												{/if}
											</div>
										{/if}
									</div>
								</div>
							{/if}
						</div>
					{/if}
					
					{#if selectedPoints.length === 0}
						<span class="text-gray-400 text-sm italic">Select tags to view trends</span>
					{/if}
				</div>
			</div>

			<!-- Divider -->
			<div class="hidden md:block w-px h-8 bg-gray-300 dark:bg-gray-700"></div>

			<!-- Time Range Selector -->
			<div class="flex items-center gap-2">
				<span class="text-sm text-gray-500 dark:text-gray-400">Range:</span>
				<div class="inline-flex rounded-lg bg-gray-100 dark:bg-gray-800 p-0.5">
					{#each timeRanges as range}
						<button
							class="px-3 py-1.5 text-sm font-medium rounded-md transition-all
								{selectedRange.hours === range.hours 
									? 'bg-naia-500 text-white shadow-sm' 
									: 'text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-200'}"
							onclick={() => changeTimeRange(range)}
						>
							{range.label}
						</button>
					{/each}
				</div>
			</div>
		</div>
	</div>

	<!-- Chart Area -->
	<div class="card overflow-hidden">
		{#if loading}
			<div class="flex items-center justify-center h-[450px] bg-gray-900/30">
				<div class="flex flex-col items-center gap-3 text-gray-400">
					
					<span>Loading trend data...</span>
				</div>
			</div>
		{:else if selectedPoints.length === 0}
			<div class="flex flex-col items-center justify-center h-[450px] text-gray-500 bg-gray-900/20">
				
				<p class="text-lg font-medium mb-1">No Tags Selected</p>
				<p class="text-sm opacity-75">Click "Add Tag" to select historian points to trend</p>
			</div>
		{:else}
			<div bind:this={chartElement} class="w-full" style="height: 450px;"></div>
		{/if}
	</div>

	<!-- Data Table -->
	{#if selectedPoints.length > 0 && trendData.length > 0}
		<div class="card overflow-hidden">
			<div class="px-4 py-3 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
				<h3 class="font-semibold text-gray-900 dark:text-gray-100">
					Data Points
					<span class="font-normal text-gray-500 text-sm ml-2">({trendData.length.toLocaleString()} samples)</span>
				</h3>
				<button 
					class="text-sm text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 transition-colors"
					onclick={() => showDataTable = !showDataTable}
				>
					{showDataTable ? 'Hide' : 'Show'}
				</button>
			</div>
			{#if showDataTable}
				<div class="max-h-80 overflow-y-auto">
					<table class="w-full text-sm">
						<thead class="bg-gray-50 dark:bg-gray-800/50 sticky top-0">
							<tr>
								<th class="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Timestamp</th>
								<th class="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Tag</th>
								<th class="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Value</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-gray-100 dark:divide-gray-800">
							{#each trendData.slice(0, 500) as row}
								<tr class="hover:bg-gray-50 dark:hover:bg-gray-800/30 transition-colors">
									<td class="px-4 py-2 font-mono text-xs text-gray-600 dark:text-gray-400">{formatTimestamp(row.timestamp)}</td>
									<td class="px-4 py-2">
										<span 
											class="inline-block w-2 h-2 rounded-full mr-2"
											style="background-color: {row.color}"
										></span>
										<span class="text-gray-900 dark:text-gray-100">{row.pointName}</span>
									</td>
									<td class="px-4 py-2 text-right font-mono text-gray-900 dark:text-gray-100">{formatValue(row.value)}</td>
								</tr>
							{/each}
						</tbody>
					</table>
					{#if trendData.length > 500}
						<div class="px-4 py-3 text-center text-sm text-gray-500 bg-gray-50 dark:bg-gray-800/50">
							Showing first 500 of {trendData.length.toLocaleString()} data points
						</div>
					{/if}
				</div>
			{/if}
		</div>
	{/if}
</div>

<!-- Click outside to close search -->
{#if showSearch}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<div class="fixed inset-0 z-10" onclick={() => showSearch = false}></div>
{/if}
