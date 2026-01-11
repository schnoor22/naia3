<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import * as echarts from 'echarts';
	import { getHistory, type HistoricalDataResponse } from '$lib/services/api';

	interface Props {
		pointId: string;
		pointName: string;
		units?: string;
	}

	let { pointId, pointName, units }: Props = $props();

	let chartContainer: HTMLDivElement;
	let chart: echarts.ECharts | null = null;
	let loading = $state(true);
	let error = $state<string | null>(null);

	// Time range options
	const timeRanges = [
		{ label: '1H', hours: 1 },
		{ label: '8H', hours: 8 },
		{ label: '24H', hours: 24 },
		{ label: '7D', hours: 168 },
	];
	let selectedRange = $state(timeRanges[0]);

	async function loadData() {
		loading = true;
		error = null;

		try {
			const end = new Date();
			const start = new Date(end.getTime() - selectedRange.hours * 60 * 60 * 1000);

			const data = await getHistory(pointId, start, end, 2000);
			renderChart(data);
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load data';
		} finally {
			loading = false;
		}
	}

	function renderChart(data: HistoricalDataResponse) {
		if (!chart) return;

		const chartData = data.data.map(d => [
			new Date(d.timestamp).getTime(),
			d.value
		]);

		const option: echarts.EChartsOption = {
			backgroundColor: 'transparent',
			grid: {
				left: 60,
				right: 20,
				top: 20,
				bottom: 40
			},
			tooltip: {
				trigger: 'axis',
				backgroundColor: 'rgba(17, 24, 39, 0.9)',
				borderColor: 'rgba(75, 85, 99, 0.5)',
				textStyle: {
					color: '#f3f4f6'
				},
				formatter: (params: any) => {
					const p = params[0];
					const date = new Date(p.data[0]);
					return `
						<div class="font-mono">
							<div class="text-gray-400">${date.toLocaleString()}</div>
							<div class="font-bold">${p.data[1]?.toFixed(2) ?? '—'} ${units || ''}</div>
						</div>
					`;
				}
			},
			xAxis: {
				type: 'time',
				axisLine: { lineStyle: { color: '#374151' } },
				axisLabel: { color: '#9ca3af' },
				splitLine: { show: false }
			},
			yAxis: {
				type: 'value',
				name: units || '',
				nameTextStyle: { color: '#9ca3af' },
				axisLine: { lineStyle: { color: '#374151' } },
				axisLabel: { color: '#9ca3af' },
				splitLine: { lineStyle: { color: '#1f2937' } }
			},
			series: [{
				name: pointName,
				type: 'line',
				data: chartData,
				smooth: false,
				symbol: 'none',
				lineStyle: {
					color: '#14b8a6',
					width: 2
				},
				areaStyle: {
					color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
						{ offset: 0, color: 'rgba(20, 184, 166, 0.3)' },
						{ offset: 1, color: 'rgba(20, 184, 166, 0)' }
					])
				}
			}],
			dataZoom: [{
				type: 'inside',
				start: 0,
				end: 100
			}]
		};

		chart.setOption(option);
	}

	onMount(() => {
		chart = echarts.init(chartContainer, 'dark');
		loadData();

		// Handle resize
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
		// Reload when range changes
		if (selectedRange && chart) {
			loadData();
		}
	});
</script>

<div class="space-y-4">
	<!-- Time range selector -->
	<div class="flex items-center gap-2">
		<span class="text-sm text-gray-500 dark:text-gray-400">Time Range:</span>
		<div class="flex gap-1 bg-gray-100 dark:bg-gray-800 rounded-lg p-1">
			{#each timeRanges as range}
				<button
					class="px-3 py-1 text-sm rounded-md transition-colors"
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

	<!-- Chart -->
	<div class="relative">
		{#if loading}
			<div class="absolute inset-0 flex items-center justify-center bg-gray-900/50 rounded-lg z-10">
				<div class="flex items-center gap-2 text-gray-400">
					<svg class="w-5 h-5 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
						<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
						<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
					</svg>
					Loading data...
				</div>
			</div>
		{/if}

		{#if error}
			<div class="absolute inset-0 flex items-center justify-center bg-gray-900/50 rounded-lg z-10">
				<div class="text-red-400">{error}</div>
			</div>
		{/if}

		<div 
			bind:this={chartContainer} 
			class="w-full h-64 rounded-lg bg-gray-900/50"
		></div>
	</div>
</div>
