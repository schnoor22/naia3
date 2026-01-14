<script lang="ts">
	import { onMount } from 'svelte';
	import { isMasterMode, getMasterHeaders } from '$lib/stores/master';
	
	interface RedisKey {
		key: string;
		type: string;
		ttl: number | null;
		value?: string;
	}
	
	let redisInfo = $state<any>(null);
	let selectedKey = $state<RedisKey | null>(null);
	let keyValue = $state<string | null>(null);
	let isLoading = $state(true);
	let error = $state<string | null>(null);
	
	onMount(loadRedisInfo);
	
	async function loadRedisInfo() {
		isLoading = true;
		error = null;
		
		try {
			const response = await fetch('/api/admin/redis', {
				headers: getMasterHeaders()
			});
			
			if (!response.ok) {
				throw new Error(`HTTP ${response.status}`);
			}
			
			redisInfo = await response.json();
		} catch (e: any) {
			error = e.message;
		} finally {
			isLoading = false;
		}
	}
</script>

<svelte:head>
	<title>Redis Browser | NAIA</title>
</svelte:head>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div class="flex items-center gap-3">
			<span class="text-2xl">🔴</span>
			<div>
				<h1 class="text-xl font-bold text-gray-900 dark:text-gray-100">Redis Browser</h1>
				<p class="text-sm text-gray-500">Current value cache inspector</p>
			</div>
		</div>
		<button
			onclick={loadRedisInfo}
			disabled={isLoading}
			class="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
		>
			{isLoading ? 'Loading...' : '🔄 Refresh'}
		</button>
	</div>
	
	{#if error}
		<div class="p-4 bg-red-900/20 border border-red-800 rounded-lg text-red-400">
			{error}
		</div>
	{/if}
	
	{#if redisInfo}
		<!-- Connection Status -->
		<div class="grid grid-cols-1 md:grid-cols-3 gap-4">
			<div class="p-4 bg-gray-900 rounded-lg border border-gray-800">
				<div class="text-sm text-gray-500 mb-1">Status</div>
				<div class="text-xl font-bold {redisInfo.connected ? 'text-emerald-400' : 'text-red-400'}">
					{redisInfo.connected ? '✅ Connected' : '❌ Disconnected'}
				</div>
			</div>
			<div class="p-4 bg-gray-900 rounded-lg border border-gray-800">
				<div class="text-sm text-gray-500 mb-1">Total Keys</div>
				<div class="text-xl font-bold text-gray-100">
					{redisInfo.keyCount?.toLocaleString() ?? 'N/A'}
				</div>
			</div>
			<div class="p-4 bg-gray-900 rounded-lg border border-gray-800">
				<div class="text-sm text-gray-500 mb-1">Endpoint</div>
				<div class="text-sm font-mono text-gray-300 truncate">
					{redisInfo.endpoints?.[0] ?? 'Unknown'}
				</div>
			</div>
		</div>
		
		<!-- Sample Keys -->
		{#if redisInfo.samples && redisInfo.samples.length > 0}
			<div class="bg-gray-900 rounded-lg border border-gray-800 overflow-hidden">
				<div class="px-4 py-3 border-b border-gray-800 bg-gray-800/50">
					<h2 class="font-medium text-gray-300">Sample Keys (first 20)</h2>
				</div>
				<div class="overflow-x-auto">
					<table class="w-full text-sm">
						<thead class="bg-gray-800">
							<tr>
								<th class="px-4 py-2 text-left text-gray-400">Key</th>
								<th class="px-4 py-2 text-left text-gray-400">Type</th>
								<th class="px-4 py-2 text-left text-gray-400">TTL</th>
							</tr>
						</thead>
						<tbody>
							{#each redisInfo.samples as sample}
								<tr class="border-b border-gray-800 hover:bg-gray-800/50">
									<td class="px-4 py-2 font-mono text-xs text-emerald-400 max-w-md truncate">
										{sample.key}
									</td>
									<td class="px-4 py-2 text-gray-400">
										<span class="px-2 py-0.5 bg-gray-700 rounded text-xs">
											{sample.type}
										</span>
									</td>
									<td class="px-4 py-2 text-gray-400">
										{#if sample.ttl === null}
											<span class="text-gray-600">No expiry</span>
										{:else}
											{Math.round(sample.ttl)}s
										{/if}
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>
		{:else if redisInfo.connected}
			<div class="p-8 text-center text-gray-500 bg-gray-900 rounded-lg border border-gray-800">
				No keys found in Redis
			</div>
		{/if}
		
		{#if !redisInfo.connected && redisInfo.error}
			<div class="p-4 bg-gray-900 rounded-lg border border-gray-800">
				<div class="text-sm text-gray-500 mb-1">Error Details</div>
				<pre class="text-xs text-red-400 font-mono">{redisInfo.error}</pre>
			</div>
		{/if}
	{:else if isLoading}
		<div class="p-8 text-center text-gray-500">
			<div class="animate-spin text-2xl mb-2">⚙️</div>
			<p>Loading Redis info...</p>
		</div>
	{/if}
	
	<!-- Info Panel -->
	<div class="bg-gray-900/50 rounded-lg border border-gray-800 p-4">
		<h3 class="text-sm font-medium text-gray-400 mb-2">About Redis in NAIA</h3>
		<div class="text-xs text-gray-500 space-y-1">
			<p>• Redis stores the <strong class="text-gray-400">current value cache</strong> for all data points</p>
			<p>• Keys are typically point IDs with their latest values</p>
			<p>• This enables instant dashboard updates without querying QuestDB</p>
			<p>• Data is automatically updated by the ingestion pipeline</p>
		</div>
	</div>
</div>
