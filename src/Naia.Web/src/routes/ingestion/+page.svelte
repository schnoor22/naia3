<script lang="ts">
	import { onMount } from 'svelte';
	import StatusCard from '$lib/components/StatusCard.svelte';
	import MetricCard from '$lib/components/MetricCard.svelte';
	import { getIngestionStatus, startIngestion, stopIngestion, checkPIHealth, discoverPIPoints, type IngestionStatus } from '$lib/services/api';
	import { toasts } from '$lib/stores/signalr';

	let status = $state<IngestionStatus | null>(null);
	let piHealth = $state<any>(null);
	let loading = $state(true);
	let actionLoading = $state<string | null>(null);

	// PI Discovery
	let discoveryFilter = $state('*BESS*');
	let discoveryResults = $state<any>(null);
	let discovering = $state(false);

	async function loadStatus() {
		try {
			const [ingestionStatus, health] = await Promise.all([
				getIngestionStatus().catch(() => null),
				checkPIHealth().catch(() => null)
			]);
			status = ingestionStatus;
			piHealth = health;
		} catch (e) {
			console.error('Failed to load status:', e);
		} finally {
			loading = false;
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
			<p class="text-gray-500 dark:text-gray-400">Manage PI System data collection</p>
		</div>
		<div class="flex items-center gap-3">
			{#if status?.isRunning}
				<button 
					class="btn btn-danger"
					onclick={handleStop}
					disabled={!!actionLoading}
				>
					{#if actionLoading === 'stop'}
						<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
							<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
							<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
						</svg>
					{:else}
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
							<path stroke-linecap="round" stroke-linejoin="round" d="M5.25 7.5A2.25 2.25 0 017.5 5.25h9a2.25 2.25 0 012.25 2.25v9a2.25 2.25 0 01-2.25 2.25h-9a2.25 2.25 0 01-2.25-2.25v-9z" />
						</svg>
					{/if}
					Stop Ingestion
				</button>
			{:else}
				<button 
					class="btn btn-success"
					onclick={handleStart}
					disabled={!!actionLoading}
				>
					{#if actionLoading === 'start'}
						<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
							<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
							<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
						</svg>
					{:else}
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
							<path stroke-linecap="round" stroke-linejoin="round" d="M5.25 5.653c0-.856.917-1.398 1.667-.986l11.54 6.348a1.125 1.125 0 010 1.971l-11.54 6.347a1.125 1.125 0 01-1.667-.985V5.653z" />
						</svg>
					{/if}
					Start Ingestion
				</button>
			{/if}
		</div>
	</div>

	<!-- Status Cards -->
	<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
		<StatusCard 
			status={status?.isRunning ? 'healthy' : 'degraded'}
			title="Pipeline Status"
			subtitle={status?.isRunning ? 'Streaming data' : 'Stopped'}
			icon="📡"
			loading={loading}
		/>
		<StatusCard 
			status={piHealth?.connected ? 'healthy' : 'unhealthy'}
			title="PI Connection"
			subtitle={piHealth?.message || 'Unknown'}
			icon="🏭"
			loading={loading}
		/>
		<MetricCard 
			title="Points Configured"
			value={status?.pointsConfigured ?? '—'}
			icon="📊"
			loading={loading}
		/>
		<MetricCard 
			title="Messages Published"
			value={status?.messagesPublished?.toLocaleString() ?? '—'}
			icon="📨"
			loading={loading}
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
								{#if status.isRunning}
									<span class="badge badge-success">Running</span>
								{:else}
									<span class="badge badge-neutral">Stopped</span>
								{/if}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Poll Interval</dt>
							<dd class="font-mono">{status.pollInterval}ms</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Last Poll</dt>
							<dd class="font-mono text-sm">
								{status.lastPollTime ? new Date(status.lastPollTime).toLocaleString() : 'Never'}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Errors</dt>
							<dd class:text-red-500={status.errors > 0} class:text-emerald-500={status.errors === 0}>
								{status.errors}
							</dd>
						</div>
					</dl>
				{:else}
					<p class="text-gray-500">Unable to load pipeline status</p>
				{/if}
			</div>
		</div>

		<!-- PI System -->
		<div class="card">
			<div class="card-header">
				<h2 class="font-semibold text-gray-900 dark:text-gray-100">PI System Connection</h2>
			</div>
			<div class="card-body">
				{#if loading}
					<div class="space-y-4">
						{#each Array(4) as _}
							<div class="skeleton h-6 w-full"></div>
						{/each}
					</div>
				{:else if piHealth}
					<dl class="space-y-4">
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Connector Type</dt>
							<dd>{piHealth.connectorType || 'Web API'}</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Connected</dt>
							<dd>
								{#if piHealth.connected}
									<span class="badge badge-success">Yes</span>
								{:else}
									<span class="badge badge-danger">No</span>
								{/if}
							</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Response Time</dt>
							<dd class="font-mono">{piHealth.responseTimeMs?.toFixed(0) ?? '—'}ms</dd>
						</div>
						<div class="flex justify-between">
							<dt class="text-gray-500 dark:text-gray-400">Message</dt>
							<dd class="text-sm text-right max-w-[200px] truncate" title={piHealth.message}>
								{piHealth.message || '—'}
							</dd>
						</div>
					</dl>
				{:else}
					<p class="text-gray-500">Unable to connect to PI System</p>
				{/if}
			</div>
		</div>
	</div>

	<!-- Point Discovery -->
	<div class="card">
		<div class="card-header">
			<h2 class="font-semibold text-gray-900 dark:text-gray-100">PI Point Discovery</h2>
		</div>
		<div class="card-body">
			<div class="flex gap-4 mb-4">
				<input
					type="text"
					class="input flex-1"
					placeholder="Filter pattern (e.g., *BESS*, *MW*)"
					bind:value={discoveryFilter}
					onkeydown={(e) => e.key === 'Enter' && handleDiscover()}
				/>
				<button 
					class="btn btn-primary"
					onclick={handleDiscover}
					disabled={discovering}
				>
					{#if discovering}
						<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
							<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
							<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
						</svg>
					{/if}
					Discover Points
				</button>
			</div>

			{#if discoveryResults}
				<div class="text-sm text-gray-500 mb-3">
					Found {discoveryResults.count} points matching "{discoveryResults.filter}"
				</div>
				<div class="table-container max-h-64 overflow-y-auto">
					<table class="table">
						<thead class="sticky top-0">
							<tr>
								<th>Tag Name</th>
								<th>Description</th>
								<th>Units</th>
								<th>Type</th>
							</tr>
						</thead>
						<tbody>
							{#each discoveryResults.points as point}
								<tr>
									<td class="font-mono text-sm">{point.SourceAddress}</td>
									<td class="max-w-[200px] truncate">{point.Description || '—'}</td>
									<td>{point.EngineeringUnits || '—'}</td>
									<td><span class="badge badge-neutral">{point.PointType}</span></td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{:else}
				<p class="text-gray-500 text-center py-8">
					Enter a filter pattern and click "Discover Points" to search PI Server
				</p>
			{/if}
		</div>
	</div>
</div>
