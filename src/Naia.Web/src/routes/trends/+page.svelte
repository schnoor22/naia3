<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import * as echarts from 'echarts';
	import { searchPoints, getHistory, type Point, type HistoricalDataResponse } from '$lib/services/api';

	let chartContainer: HTMLDivElement;
	let chart: echarts.ECharts | null = null;
	let loading = $state(false);
	let searchLoading = $state(false);

	// Point selection
	let searchQuery = $state('');
	let searchResults = $state<Point[]>([]);
	let selectedPoints = $state<Point[]>([]);
	let showSearch = $state(false);

	// Time range
	const timeRanges = [
		{ label: '1 Hour', hours: 1 },
		{ label: '8 Hours', hours: 8 },
		{ label: '24 Hours', hours: 24 },
		{ label: '7 Days', hours: 168 },
		{ label: '30 Days', hours: 720 },
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

	async function searchTags() {
		if (!searchQuery.trim()) {
			searchResults = [];
			return;
		}

		searchLoading = true;
		try {
			const result = await searchPoints({ tagName: searchQuery, take: 20 });
			searchResults = result.data.filter(p => p.pointSequenceId && !selectedPoints.find(s => s.id === p.id));
		} catch (e) {
			console.error('Search failed:', e);
		} finally {
			searchLoading = false;
		}
	}

	function addPoint(point: Point) {
		if (selectedPoints.length >= 8) {
			return; // Max 8 series
		}
		selectedPoints = [...selectedPoints, point];
		searchQuery = '';
		searchResults = [];
		showSearch = false;
		loadChartData();
	}

	function removePoint(point: Point) {
		selectedPoints = selectedPoints.filter(p => p.id !== point.id);
		loadChartData();
	}

	async function loadChartData() {
		if (selectedPoints.length === 0 || !chart) {
			chart?.clear();
			return;
		}

		loading = true;

		try {
			const end = new Date();
			const start = new Date(end.getTime() - selectedRange.hours * 60 * 60 * 1000);

			// Load data for all selected points in parallel
			const dataPromises = selectedPoints.map(point =>
				getHistory(point.id, start, end, 2000).catch(() => null)
			);

			const results = await Promise.all(dataPromises);
			renderChart(results, selectedPoints);
		} catch (e) {
			console.error('Failed to load chart data:', e);
		} finally {
			loading = false;
		}
	}

	function renderChart(dataResults: (HistoricalDataResponse | null)[], points: Point[]) {
		if (!chart) return;

		const series: echarts.SeriesOption[] = [];
		const legendData: string[] = [];

		dataResults.forEach((data, index) => {
			if (!data) return;

			const point = points[index];
			const color = seriesColors[index % seriesColors.length];

			legendData.push(point.name);

			series.push({
				name: point.name,
				type: 'line',
				data: data.data.map(d => [new Date(d.timestamp).getTime(), d.value]),
				smooth: false,
				symbol: 'none',
				lineStyle: { color, width: 2 },
				emphasis: { lineStyle: { width: 3 } }
			});
		});

		const option: echarts.EChartsOption = {
			backgroundColor: 'transparent',
			legend: {
				data: legendData,
				textStyle: { color: '#9ca3af' },
				top: 0,
				type: 'scroll'
			},
			grid: {
				left: 60,
				right: 40,
				top: 40,
				bottom: 60
			},
			tooltip: {
				trigger: 'axis',
				backgroundColor: 'rgba(17, 24, 39, 0.95)',
				borderColor: 'rgba(75, 85, 99, 0.5)',
				textStyle: { color: '#f3f4f6' },
				axisPointer: {
					type: 'cross',
					label: { backgroundColor: '#374151' }
				}
			},
			toolbox: {
				feature: {
					dataZoom: { yAxisIndex: 'none' },
					restore: {},
					saveAsImage: {}
				},
				iconStyle: { borderColor: '#9ca3af' }
			},
			xAxis: {
				type: 'time',
				axisLine: { lineStyle: { color: '#374151' } },
				axisLabel: { color: '#9ca3af' },
				splitLine: { show: false }
			},
			yAxis: {
				type: 'value',
				axisLine: { lineStyle: { color: '#374151' } },
				axisLabel: { color: '#9ca3af' },
				splitLine: { lineStyle: { color: '#1f2937' } }
			},
			dataZoom: [
				{ type: 'inside', start: 0, end: 100 },
				{ type: 'slider', start: 0, end: 100, height: 20, bottom: 10 }
			],
			series
		};

		chart.setOption(option, true);
	}

	onMount(() => {
		chart = echarts.init(chartContainer, 'dark');

		const resizeObserver = new ResizeObserver(() => {
			chart?.resize();
		});
		resizeObserver.observe(chartContainer);

		return () => {
			resizeObserver.disconnect();
			chart?.dispose();
		};
	});

	$effect(() => {
		if (selectedRange && selectedPoints.length > 0) {
			loadChartData();
		}
	});
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">Trend Viewer</h1>
			<p class="text-gray-500 dark:text-gray-400">Compare multiple historian tags over time</p>
		</div>
	</div>

	<!-- Controls -->
	<div class="card p-4">
		<div class="flex flex-wrap items-start gap-4">
			<!-- Tag Selection -->
			<div class="flex-1 min-w-[300px]">
				<label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
					Selected Tags ({selectedPoints.length}/8)
				</label>
				<div class="flex flex-wrap gap-2 min-h-[40px]">
					{#each selectedPoints as point, index}
						<span 
							class="inline-flex items-center gap-1 px-3 py-1 rounded-full text-sm font-medium text-white"
							style="background-color: {seriesColors[index % seriesColors.length]}"
						>
							{point.name}
							<button 
								class="ml-1 hover:bg-white/20 rounded-full p-0.5"
								onclick={() => removePoint(point)}
							>
								<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3 h-3">
									<path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
								</svg>
							</button>
						</span>
					{/each}
					{#if selectedPoints.length < 8}
						<div class="relative">
							<button 
								class="inline-flex items-center gap-1 px-3 py-1 rounded-full text-sm border-2 border-dashed border-gray-300 dark:border-gray-600 text-gray-500 hover:border-naia-500 hover:text-naia-500 transition-colors"
								onclick={() => showSearch = !showSearch}
							>
								<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4">
									<path stroke-linecap="round" stroke-linejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
								</svg>
								Add Tag
							</button>

							{#if showSearch}
								<div class="absolute top-full left-0 mt-2 w-80 bg-white dark:bg-gray-800 rounded-lg shadow-xl border border-gray-200 dark:border-gray-700 z-10">
									<div class="p-2">
										<input
											type="text"
											class="input"
											placeholder="Search tags..."
											bind:value={searchQuery}
											oninput={searchTags}
											autofocus
										/>
									</div>
									<div class="max-h-60 overflow-y-auto">
										{#if searchLoading}
											<div class="p-4 text-center text-gray-500">
												<svg class="w-5 h-5 animate-spin mx-auto" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
													<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
													<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
												</svg>
											</div>
										{:else if searchResults.length > 0}
											{#each searchResults as result}
												<button
													class="w-full px-3 py-2 text-left hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors"
													onclick={() => addPoint(result)}
												>
													<div class="font-mono text-sm">{result.name}</div>
													{#if result.description}
														<div class="text-xs text-gray-500 truncate">{result.description}</div>
													{/if}
												</button>
											{/each}
										{:else if searchQuery}
											<div class="p-4 text-center text-gray-500 text-sm">
												No tags found
											</div>
										{:else}
											<div class="p-4 text-center text-gray-500 text-sm">
												Type to search for tags
											</div>
										{/if}
									</div>
								</div>
							{/if}
						</div>
					{/if}
				</div>
			</div>

			<!-- Time Range -->
			<div>
				<label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
					Time Range
				</label>
				<div class="flex gap-1 bg-gray-100 dark:bg-gray-800 rounded-lg p-1">
					{#each timeRanges as range}
						<button
							class="px-3 py-1.5 text-sm rounded-md transition-colors whitespace-nowrap"
							class:bg-naia-500={selectedRange === range}
							class:text-white={selectedRange === range}
							class:text-gray-600={selectedRange !== range}
							class:dark:text-gray-400={selectedRange !== range}
							onclick={() => selectedRange = range}
						>
							{range.label}
						</button>
					{/each}
				</div>
			</div>
		</div>
	</div>

	<!-- Chart -->
	<div class="card overflow-hidden">
		<div class="relative">
			{#if loading}
				<div class="absolute inset-0 flex items-center justify-center bg-gray-900/50 z-10">
					<div class="flex items-center gap-2 text-gray-400">
						<svg class="w-5 h-5 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
							<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
							<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
						</svg>
						Loading data...
					</div>
				</div>
			{/if}

			{#if selectedPoints.length === 0}
				<div class="flex flex-col items-center justify-center h-96 text-gray-500">
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-12 h-12 mb-4 opacity-50">
						<path stroke-linecap="round" stroke-linejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z" />
					</svg>
					<p class="text-lg font-medium">No tags selected</p>
					<p class="text-sm">Click "Add Tag" above to select historian points to trend</p>
				</div>
			{/if}

			<div 
				bind:this={chartContainer} 
				class="w-full h-96"
				class:invisible={selectedPoints.length === 0}
			></div>
		</div>
	</div>

	<!-- Tips -->
	<div class="text-sm text-gray-500 dark:text-gray-400">
		<p>💡 <strong>Tips:</strong> Use mouse wheel to zoom, drag to pan. Click legend items to show/hide series.</p>
	</div>
</div>

<!-- Click outside to close search -->
{#if showSearch}
	<div class="fixed inset-0 z-0" onclick={() => showSearch = false}></div>
{/if}
