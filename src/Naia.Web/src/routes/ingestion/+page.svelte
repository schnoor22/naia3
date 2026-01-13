<script lang="ts">
	import { onMount } from 'svelte';
	import StatusCard from '$lib/components/StatusCard.svelte';
	import MetricCard from '$lib/components/MetricCard.svelte';
	import { getIngestionStatus, startIngestion, stopIngestion, checkPIHealth, discoverPIPoints, addPIPoints, type IngestionStatus } from '$lib/services/api';
	import { toasts } from '$lib/stores/signalr';

	let status = $state<IngestionStatus | null>(null);
	let piHealth = $state<any>(null);
	let loading = $state(true);
	let actionLoading = $state<string | null>(null);
	let isInitialLoad = $state(true);

	// Cache to detect changes and prevent clearing discovery results
	let lastStatusJson = '';
	let lastHealthJson = '';

	// PI Discovery
	let discoveryFilter = $state('*BESS*');
	let discoveryResults = $state<any>(null);
	let discovering = $state(false);
	let selectedPoints = $state<Set<string>>(new Set());
	let addingPoints = $state(false);

	async function loadStatus() {
		try {
			const [ingestionStatus, health] = await Promise.all([
				getIngestionStatus().catch(() => null),
				checkPIHealth().catch(() => null)
			]);

			// Only update status if it changed
			const statusJson = JSON.stringify(ingestionStatus);
			if (statusJson !== lastStatusJson) {
				status = ingestionStatus;
				lastStatusJson = statusJson;
			}

			// Only update health if changed
			const healthJson = JSON.stringify(health);
			if (healthJson !== lastHealthJson) {
				piHealth = health;
				lastHealthJson = healthJson;
			}
		} catch (e) {
			console.error('Failed to load status:', e);
		} finally {
			loading = false;
			isInitialLoad = false;
		}
	}

	async function handleStart() {
		actionLoading = 'start';
		try {
			await startIngestion();
			toasts.add({
				type: 'success',
				title: 'Ingestion Started',
				message: 'PI → Kafka pipeline is now running'
			});
			await loadStatus();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Failed to start',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			actionLoading = null;
		}
	}

	async function handleStop() {
		actionLoading = 'stop';
		try {
			await stopIngestion();
			toasts.add({
				type: 'info',
				title: 'Ingestion Stopped',
				message: 'Pipeline has been stopped'
			});
			await loadStatus();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Failed to stop',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			actionLoading = null;
		}
	}

	async function handleDiscover() {
		discovering = true;
		try {
			discoveryResults = await discoverPIPoints(discoveryFilter, 100);
			selectedPoints = new Set();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Discovery failed',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			discovering = false;
		}
	}

	function togglePointSelection(sourceAddress: string) {
		const point = discoveryResults.points.find((p: any) => (p.sourceAddress || p.name) === sourceAddress);
		if (point?.existsInDatabase) return; // Don't allow selecting existing points
		
		const newSet = new Set(selectedPoints);
		if (newSet.has(sourceAddress)) {
			newSet.delete(sourceAddress);
		} else {
			newSet.add(sourceAddress);
		}
		selectedPoints = newSet;
	}

	function toggleSelectAll() {
		// Only select points that don't exist in database
		const availablePoints = discoveryResults.points.filter((p: any) => !p.existsInDatabase);
		if (selectedPoints.size === availablePoints.length) {
			selectedPoints = new Set();
		} else {
			selectedPoints = new Set(availablePoints.map((p: any) => p.sourceAddress || p.name));
		}
	}

	async function handleAddPoints() {
		if (selectedPoints.size === 0) {
			toasts.add({
				type: 'warning',
				title: 'No points selected',
				message: 'Please select at least one point to add'
			});
			return;
		}

		addingPoints = true;
		try {
			const pointsToAdd = discoveryResults.points.filter(
				(p: any) => selectedPoints.has(p.sourceAddress || p.name)
			);
			
			await addPIPoints(pointsToAdd);
			
			toasts.add({
				type: 'success',
				title: 'Points added',
				message: `${selectedPoints.size} point(s) added to ingestion pipeline`
			});
			
			selectedPoints.clear();
			discoveryResults = null;
			await loadStatus();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Failed to add points',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			addingPoints = false;
		}
	}

	onMount(() => {
		loadStatus();
		const interval = setInterval(loadStatus, 5000);
		return () => clearInterval(interval);
	});
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">Ingestion Control</h1>
			<p class="text-gray-500 dark:text-gray-400">Wind Farm Replay Data Ingestion</p>
		</div>
	</div>

	<!-- Status Cards -->
	<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
		<StatusCard 
			status={status?.system?.isRunning ? 'healthy' : 'degraded'}
			title="Pipeline Status"
			subtitle={status?.system?.isRunning ? 'Streaming data' : 'Stopped'}
			icon="📡"
			loading={isInitialLoad}
		/>
		<StatusCard 
			status={status?.replayWorker?.isEnabled ? 'healthy' : 'unhealthy'}
			title="Replay Worker"
			subtitle={status?.replayWorker?.description || 'Wind Farm Replay'}
			icon="🏭"
			loading={isInitialLoad}
		/>
		<MetricCard 
			title="Data Source"
			value={status?.replayWorker?.dataSource?.split('/').pop() ?? '—'}
			icon="📊"
			loading={isInitialLoad}
		/>
		<MetricCard 
			title="Active Connector"
			value={status?.replayWorker?.isEnabled ? 'Replay Worker' : 'None'}
			icon="📨"
			loading={isInitialLoad}
		/>
	</div>

	<!-- Pipeline Details -->
	<div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
		<!-- Current Status -->
		<div class="card">
			<div class="card-header">
				<h2 class="font-semibold text-gray-900 dark:text-gray-100">Pipeline Details</h2>
			</div>
			<div class="card-body">
				{#if loading}
					<div class="space-y-4">
						{#each Array(4) as _}
							<div class="skeleton h-6 w-full"></div>
						{/each}
					</div>
				{:else if status}
					<dl class="space-y-4">
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Status</dt>
							<dd>
								{#if status.system?.isRunning}
									<span class="badge badge-success">Running</span>
								{:else}
									<span class="badge badge-neutral">Stopped</span>
								{/if}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Data Flow</dt>
							<dd class="text-sm font-mono text-right max-w-xs break-words">
								{status.system?.dataFlow || 'Unknown'}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Active Sources</dt>
							<dd class="text-sm text-right">
								{status.system?.activeConnectors || 'None'}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Note</dt>
							<dd class="text-xs text-gray-500 text-right max-w-sm">
								{status.system?.note || ''}
							</dd>
						</div>
					</dl>
				{:else}
					<p class="text-gray-500">Unable to load pipeline status</p>
				{/if}
			</div>
		</div>

		<!-- Replay Worker Details -->
		<div class="card">
			<div class="card-header">
				<h2 class="font-semibold text-gray-900 dark:text-gray-100">Replay Worker Details</h2>
			</div>
			<div class="card-body">
				{#if loading}
					<div class="space-y-4">
						{#each Array(4) as _}
							<div class="skeleton h-6 w-full"></div>
						{/each}
					</div>
				{:else if status?.replayWorker}
					<dl class="space-y-4">
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Status</dt>
							<dd>
								{#if status.replayWorker.isEnabled}
									<span class="badge badge-success">Enabled</span>
								{:else}
									<span class="badge badge-neutral">Disabled</span>
								{/if}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Description</dt>
							<dd class="text-sm text-right max-w-xs">
								{status.replayWorker.description || '—'}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Data Source</dt>
							<dd class="text-sm font-mono text-right break-all">
								{status.replayWorker.dataSource || '—'}
							</dd>
						</div>
						<div class="flex justify-between items-start">
							<dt class="text-gray-500 dark:text-gray-400">Monitor</dt>
							<dd class="text-xs text-gray-500 text-right font-mono max-w-sm break-words">
								{status.replayWorker.status || '—'}
							</dd>
						</div>
					</dl>
				{:else}
					<p class="text-gray-500">Replay worker information unavailable</p>
				{/if}
			</div>
		</div>
	</div>

	<!-- System Information -->
	<div class="card">
		<div class="card-header">
			<h2 class="font-semibold text-gray-900 dark:text-gray-100">System Information</h2>
		</div>
		<div class="card-body">
			<div class="space-y-4">
				<div>
					<h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-2">About the Replay Worker</h3>
					<p class="text-sm text-gray-600 dark:text-gray-400">
						The Wind Farm Replay Worker simulates historical wind farm operations by replaying real SCADA data 
						from the Kelmarsh Wind Farm. This data flows through Kafka to the ingestion worker, which processes 
						and stores it in QuestDB for analysis.
					</p>
				</div>
				<div>
					<h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-2">Data Flow</h3>
					<p class="text-sm text-gray-600 dark:text-gray-400 font-mono">
						{status?.system?.dataFlow || 'Replay Worker → Kafka → Ingestion Worker → QuestDB'}
					</p>
				</div>
				{#if status?.system?.note}
					<div class="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-3">
						<p class="text-xs text-blue-800 dark:text-blue-300">
							ℹ️ {status.system.note}
						</p>
					</div>
				{/if}
			</div>
		</div>
	</div>
</div>
