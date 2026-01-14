<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	
	interface KafkaTopic {
		name: string;
		partitions: number;
		replicationFactor: number;
		messageCount?: number;
	}
	
	interface ConsumerGroup {
		groupId: string;
		topics: string[];
		lag: number;
		state: string;
	}
	
	interface KafkaMessage {
		topic: string;
		partition: number;
		offset: number;
		timestamp: string;
		key?: string;
		value: string;
	}
	
	let topics = $state<KafkaTopic[]>([]);
	let consumerGroups = $state<ConsumerGroup[]>([]);
	let messages = $state<KafkaMessage[]>([]);
	let selectedTopic = $state<string | null>(null);
	let isLoading = $state(false);
	let error = $state<string | null>(null);
	let autoRefresh = $state(true);
	let refreshInterval: ReturnType<typeof setInterval>;
	
	onMount(async () => {
		await loadTopics();
		if (autoRefresh) {
			refreshInterval = setInterval(loadTopics, 10000);
		}
	});
	
	onDestroy(() => {
		if (refreshInterval) clearInterval(refreshInterval);
	});
	
	async function loadTopics() {
		isLoading = true;
		error = null;
		
		try {
			const response = await fetch('/api/pipeline/kafka/topics');
			if (response.ok) {
				const data = await response.json();
				topics = data.topics || [];
				consumerGroups = data.consumerGroups || [];
			} else {
				error = `Failed to load topics: HTTP ${response.status}`;
			}
		} catch (e: any) {
			error = e.message || 'Failed to connect';
		} finally {
			isLoading = false;
		}
	}
	
	async function selectTopic(topic: string) {
		selectedTopic = topic;
		isLoading = true;
		
		try {
			const response = await fetch(`/api/pipeline/kafka/messages?topic=${encodeURIComponent(topic)}&limit=50`);
			if (response.ok) {
				const data = await response.json();
				messages = data.messages || [];
			} else {
				messages = [];
			}
		} catch {
			messages = [];
		} finally {
			isLoading = false;
		}
	}
	
	function formatValue(value: string): string {
		try {
			const parsed = JSON.parse(value);
			return JSON.stringify(parsed, null, 2);
		} catch {
			return value;
		}
	}
	
	function getTotalLag(): number {
		return consumerGroups.reduce((sum, g) => sum + g.lag, 0);
	}
</script>

<svelte:head>
	<title>Kafka Topics | NAIA</title>
</svelte:head>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-semibold text-white flex items-center gap-3">
				<span class="text-3xl">📨</span>
				Kafka Topic Viewer
			</h1>
			<p class="text-slate-400 mt-1">Monitor messages in flight and consumer lag</p>
		</div>
		<div class="flex items-center gap-4">
			<label class="flex items-center gap-2 text-sm text-slate-400">
				<input 
					type="checkbox" 
					bind:checked={autoRefresh}
					onchange={() => {
						if (autoRefresh) {
							refreshInterval = setInterval(loadTopics, 10000);
						} else if (refreshInterval) {
							clearInterval(refreshInterval);
						}
					}}
					class="rounded border-slate-600 bg-slate-800 text-teal-500 focus:ring-teal-500"
				/>
				Auto-refresh
			</label>
			<button
				onclick={loadTopics}
				disabled={isLoading}
				class="px-4 py-2 bg-slate-800 hover:bg-slate-700 border border-slate-700 rounded-lg text-white text-sm transition-colors flex items-center gap-2 disabled:opacity-50"
			>
				<span class={isLoading ? 'animate-spin' : ''}>🔄</span>
				Refresh
			</button>
		</div>
	</div>
	
	{#if error}
		<div class="bg-red-500/10 border border-red-500/30 rounded-lg p-4 text-red-400" in:fade>
			{error}
		</div>
	{/if}
	
	<!-- Stats Overview -->
	<div class="grid grid-cols-1 md:grid-cols-4 gap-4">
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 p-4">
			<div class="text-3xl font-bold text-white">{topics.length}</div>
			<div class="text-sm text-slate-400">Topics</div>
		</div>
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 p-4">
			<div class="text-3xl font-bold text-white">{consumerGroups.length}</div>
			<div class="text-sm text-slate-400">Consumer Groups</div>
		</div>
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 p-4">
			<div class="text-3xl font-bold {getTotalLag() > 1000 ? 'text-amber-400' : 'text-white'}">{getTotalLag().toLocaleString()}</div>
			<div class="text-sm text-slate-400">Total Lag</div>
		</div>
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 p-4">
			<div class="text-3xl font-bold text-white">{messages.length}</div>
			<div class="text-sm text-slate-400">Messages Loaded</div>
		</div>
	</div>
	
	<div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
		<!-- Topics List -->
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
			<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
				<h2 class="font-medium text-white">Topics</h2>
			</div>
			<div class="max-h-96 overflow-y-auto">
				{#if topics.length === 0}
					<div class="p-4 text-center text-slate-500">
						{isLoading ? 'Loading...' : 'No topics found'}
					</div>
				{:else}
					{#each topics as topic (topic.name)}
						<button
							onclick={() => selectTopic(topic.name)}
							class="w-full px-4 py-3 text-left hover:bg-slate-800/50 border-b border-slate-700/30 transition-colors {selectedTopic === topic.name ? 'bg-teal-500/10 border-l-2 border-l-teal-500' : ''}"
						>
							<div class="font-medium text-white truncate">{topic.name}</div>
							<div class="text-xs text-slate-500 mt-1">
								{topic.partitions} partitions
							</div>
						</button>
					{/each}
				{/if}
			</div>
		</div>
		
		<!-- Consumer Groups -->
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
			<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
				<h2 class="font-medium text-white">Consumer Groups</h2>
			</div>
			<div class="max-h-96 overflow-y-auto">
				{#if consumerGroups.length === 0}
					<div class="p-4 text-center text-slate-500">
						{isLoading ? 'Loading...' : 'No consumer groups'}
					</div>
				{:else}
					{#each consumerGroups as group (group.groupId)}
						<div class="px-4 py-3 border-b border-slate-700/30">
							<div class="flex items-center justify-between">
								<span class="font-medium text-white truncate">{group.groupId}</span>
								<span class="px-2 py-0.5 rounded text-xs {group.state === 'Stable' ? 'bg-emerald-500/20 text-emerald-400' : 'bg-amber-500/20 text-amber-400'}">
									{group.state}
								</span>
							</div>
							<div class="flex items-center justify-between mt-2 text-sm">
								<span class="text-slate-500">{group.topics.length} topics</span>
								<span class="{group.lag > 100 ? 'text-amber-400' : 'text-slate-400'}">
									Lag: {group.lag.toLocaleString()}
								</span>
							</div>
						</div>
					{/each}
				{/if}
			</div>
		</div>
		
		<!-- Messages Preview -->
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
			<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
				<h2 class="font-medium text-white">
					{selectedTopic ? `Messages: ${selectedTopic}` : 'Select a topic'}
				</h2>
			</div>
			<div class="max-h-96 overflow-y-auto">
				{#if !selectedTopic}
					<div class="p-4 text-center text-slate-500">
						Select a topic to view messages
					</div>
				{:else if messages.length === 0}
					<div class="p-4 text-center text-slate-500">
						{isLoading ? 'Loading messages...' : 'No messages found'}
					</div>
				{:else}
					{#each messages as msg, i (msg.offset)}
						<div 
							class="px-4 py-3 border-b border-slate-700/30 hover:bg-slate-800/30"
							in:fly={{ x: 20, delay: i * 20, duration: 200 }}
						>
							<div class="flex items-center justify-between text-xs text-slate-500 mb-2">
								<span>Offset: {msg.offset}</span>
								<span>P{msg.partition}</span>
							</div>
							{#if msg.key}
								<div class="text-xs text-teal-400 mb-1">Key: {msg.key}</div>
							{/if}
							<pre class="text-xs text-slate-300 bg-slate-800/50 rounded p-2 overflow-x-auto max-h-24">{formatValue(msg.value)}</pre>
							<div class="text-xs text-slate-600 mt-1">{msg.timestamp}</div>
						</div>
					{/each}
				{/if}
			</div>
		</div>
	</div>
</div>
