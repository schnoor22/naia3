<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, scale, fly } from 'svelte/transition';
	
	interface ServiceStatus {
		name: string;
		displayName: string;
		icon: string;
		status: 'healthy' | 'degraded' | 'unhealthy' | 'unknown' | 'checking';
		latency?: number;
		message?: string;
		details?: Record<string, any>;
		lastCheck: Date;
	}
	
	interface DataFlowNode {
		id: string;
		label: string;
		icon: string;
		status: 'active' | 'idle' | 'error';
		throughput?: number;
	}
	
	let services = $state<ServiceStatus[]>([
		{ name: 'api', displayName: 'NAIA API', icon: '🚀', status: 'checking', lastCheck: new Date() },
		{ name: 'postgresql', displayName: 'PostgreSQL', icon: '🐘', status: 'checking', lastCheck: new Date() },
		{ name: 'questdb', displayName: 'QuestDB', icon: '⏱️', status: 'checking', lastCheck: new Date() },
		{ name: 'redis', displayName: 'Redis', icon: '⚡', status: 'checking', lastCheck: new Date() },
		{ name: 'kafka', displayName: 'Kafka', icon: '📨', status: 'checking', lastCheck: new Date() },
		{ name: 'signalr', displayName: 'SignalR Hub', icon: '📡', status: 'checking', lastCheck: new Date() },
		{ name: 'ingestion', displayName: 'Data Ingestion', icon: '🔄', status: 'checking', lastCheck: new Date() },
	]);
	
	let dataFlow = $state<DataFlowNode[]>([
		{ id: 'csv', label: 'CSV Files', icon: '📄', status: 'idle' },
		{ id: 'kafka', label: 'Kafka', icon: '📨', status: 'idle' },
		{ id: 'ingestion', label: 'Ingestion', icon: '⚙️', status: 'idle' },
		{ id: 'questdb', label: 'QuestDB', icon: '⏱️', status: 'idle' },
		{ id: 'api', label: 'API', icon: '🚀', status: 'idle' },
		{ id: 'ui', label: 'UI/SignalR', icon: '📡', status: 'idle' },
	]);
	
	let refreshInterval: ReturnType<typeof setInterval>;
	let isRefreshing = $state(false);
	let lastFullRefresh = $state<Date>(new Date());
	let systemScore = $state(0);
	
	onMount(() => {
		checkAllServices();
		refreshInterval = setInterval(checkAllServices, 30000);
	});
	
	onDestroy(() => {
		if (refreshInterval) clearInterval(refreshInterval);
	});
	
	async function checkAllServices() {
		isRefreshing = true;
		
		await Promise.all([
			checkApi(),
			checkPostgres(),
			checkQuestDb(),
			checkRedis(),
			checkKafka(),
			checkSignalR(),
			checkIngestion()
		]);
		
		lastFullRefresh = new Date();
		isRefreshing = false;
		
		// Calculate system score
		const healthyCount = services.filter(s => s.status === 'healthy').length;
		const degradedCount = services.filter(s => s.status === 'degraded').length;
		systemScore = Math.round(((healthyCount + degradedCount * 0.5) / services.length) * 100);
	}
	
	async function checkApi() {
		const idx = services.findIndex(s => s.name === 'api');
		const start = performance.now();
		
		try {
			const response = await fetch('/api/health');
			const latency = Math.round(performance.now() - start);
			
			if (response.ok) {
				const data = await response.json();
				services[idx] = {
					...services[idx],
					status: 'healthy',
					latency,
					message: 'API responding normally',
					details: data,
					lastCheck: new Date()
				};
				
				// Update dataFlow
				dataFlow[4] = { ...dataFlow[4], status: 'active' };
				dataFlow[5] = { ...dataFlow[5], status: 'active' };
			} else {
				services[idx] = {
					...services[idx],
					status: 'degraded',
					latency,
					message: `HTTP ${response.status}`,
					lastCheck: new Date()
				};
			}
		} catch (e: any) {
			services[idx] = {
				...services[idx],
				status: 'unhealthy',
				message: e.message || 'Connection failed',
				lastCheck: new Date()
			};
		}
	}
	
	async function checkPostgres() {
		const idx = services.findIndex(s => s.name === 'postgresql');
		try {
			const response = await fetch('/api/health');
			if (response.ok) {
				const data = await response.json();
				const pgStatus = data.checks?.postgresql;
				services[idx] = {
					...services[idx],
					status: pgStatus?.status === 'Healthy' ? 'healthy' : 'degraded',
					message: pgStatus?.status || 'Status unknown',
					lastCheck: new Date()
				};
			}
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', lastCheck: new Date() };
		}
	}
	
	async function checkQuestDb() {
		const idx = services.findIndex(s => s.name === 'questdb');
		try {
			const response = await fetch('/api/health');
			if (response.ok) {
				const data = await response.json();
				const qdbStatus = data.checks?.questdb;
				services[idx] = {
					...services[idx],
					status: qdbStatus?.status === 'Healthy' ? 'healthy' : 'degraded',
					message: qdbStatus?.status || 'Status unknown',
					details: qdbStatus,
					lastCheck: new Date()
				};
				dataFlow[3] = { ...dataFlow[3], status: qdbStatus?.status === 'Healthy' ? 'active' : 'error' };
			}
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', lastCheck: new Date() };
		}
	}
	
	async function checkRedis() {
		const idx = services.findIndex(s => s.name === 'redis');
		try {
			const response = await fetch('/api/health');
			if (response.ok) {
				const data = await response.json();
				const redisStatus = data.checks?.redis;
				services[idx] = {
					...services[idx],
					status: redisStatus?.status === 'Healthy' ? 'healthy' : 'degraded',
					message: redisStatus?.status || 'Status unknown',
					lastCheck: new Date()
				};
			}
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', lastCheck: new Date() };
		}
	}
	
	async function checkKafka() {
		const idx = services.findIndex(s => s.name === 'kafka');
		try {
			const response = await fetch('/api/pipeline/health');
			if (response.ok) {
				const data = await response.json();
				services[idx] = {
					...services[idx],
					status: data.kafkaConnected ? 'healthy' : 'unhealthy',
					message: data.kafkaConnected ? 'Connected' : 'Disconnected',
					details: data,
					lastCheck: new Date()
				};
				dataFlow[1] = { ...dataFlow[1], status: data.kafkaConnected ? 'active' : 'error' };
			}
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', message: 'Cannot reach pipeline API', lastCheck: new Date() };
		}
	}
	
	async function checkSignalR() {
		const idx = services.findIndex(s => s.name === 'signalr');
		try {
			// Check if hub negotiation works
			const response = await fetch('/hubs/data/negotiate?negotiateVersion=1', { method: 'POST' });
			services[idx] = {
				...services[idx],
				status: response.ok ? 'healthy' : 'degraded',
				message: response.ok ? 'Hub available' : `HTTP ${response.status}`,
				lastCheck: new Date()
			};
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', message: 'Cannot negotiate', lastCheck: new Date() };
		}
	}
	
	async function checkIngestion() {
		const idx = services.findIndex(s => s.name === 'ingestion');
		try {
			const response = await fetch('/api/pipeline/health');
			if (response.ok) {
				const data = await response.json();
				services[idx] = {
					...services[idx],
					status: data.isHealthy ? 'healthy' : 'degraded',
					message: data.isHealthy ? 'Processing normally' : 'Issues detected',
					details: data,
					lastCheck: new Date()
				};
				dataFlow[0] = { ...dataFlow[0], status: 'active' };
				dataFlow[2] = { ...dataFlow[2], status: data.isHealthy ? 'active' : 'error' };
			}
		} catch {
			services[idx] = { ...services[idx], status: 'unknown', lastCheck: new Date() };
		}
	}
	
	function getStatusColor(status: string) {
		switch (status) {
			case 'healthy': return 'from-emerald-500 to-emerald-600';
			case 'degraded': return 'from-amber-500 to-amber-600';
			case 'unhealthy': return 'from-red-500 to-red-600';
			case 'checking': return 'from-slate-500 to-slate-600';
			default: return 'from-slate-500 to-slate-600';
		}
	}
	
	function getStatusBg(status: string) {
		switch (status) {
			case 'healthy': return 'bg-emerald-500/10 border-emerald-500/30';
			case 'degraded': return 'bg-amber-500/10 border-amber-500/30';
			case 'unhealthy': return 'bg-red-500/10 border-red-500/30';
			default: return 'bg-slate-500/10 border-slate-500/30';
		}
	}
	
	function getFlowColor(status: string) {
		switch (status) {
			case 'active': return 'bg-emerald-500/20 border-emerald-500/40 text-emerald-400';
			case 'error': return 'bg-red-500/20 border-red-500/40 text-red-400';
			default: return 'bg-slate-500/20 border-slate-500/40 text-slate-400';
		}
	}
	
	function getScoreColor(score: number) {
		if (score >= 80) return 'text-emerald-400';
		if (score >= 50) return 'text-amber-400';
		return 'text-red-400';
	}
</script>

<svelte:head>
	<title>System Health | NAIA</title>
</svelte:head>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-semibold text-white flex items-center gap-3">
				<span class="text-3xl">💚</span>
				System Health Matrix
			</h1>
			<p class="text-slate-400 mt-1">Real-time monitoring of all NAIA services</p>
		</div>
		<div class="flex items-center gap-4">
			<div class="text-right">
				<p class="text-xs text-slate-500">Last updated</p>
				<p class="text-sm text-slate-300">{lastFullRefresh.toLocaleTimeString()}</p>
			</div>
			<button
				onclick={checkAllServices}
				disabled={isRefreshing}
				class="px-4 py-2 bg-slate-800 hover:bg-slate-700 border border-slate-700 rounded-lg text-white text-sm transition-colors flex items-center gap-2 disabled:opacity-50"
			>
				<span class={isRefreshing ? 'animate-spin' : ''}>🔄</span>
				{isRefreshing ? 'Checking...' : 'Refresh'}
			</button>
		</div>
	</div>
	
	<!-- System Score Card -->
	<div class="bg-gradient-to-r from-slate-900 via-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 p-6 shadow-xl">
		<div class="flex items-center justify-between">
			<div>
				<h2 class="text-lg font-medium text-white mb-1">System Health Score</h2>
				<p class="text-sm text-slate-400">Overall system availability and performance</p>
			</div>
			<div class="text-right">
				<span class="text-5xl font-bold {getScoreColor(systemScore)}">{systemScore}%</span>
				<p class="text-sm text-slate-500 mt-1">
					{services.filter(s => s.status === 'healthy').length}/{services.length} services healthy
				</p>
			</div>
		</div>
		
		<!-- Progress bar -->
		<div class="mt-4 h-2 bg-slate-800 rounded-full overflow-hidden">
			<div 
				class="h-full bg-gradient-to-r {systemScore >= 80 ? 'from-emerald-500 to-emerald-400' : systemScore >= 50 ? 'from-amber-500 to-amber-400' : 'from-red-500 to-red-400'} transition-all duration-500"
				style="width: {systemScore}%"
			></div>
		</div>
	</div>
	
	<!-- Service Grid -->
	<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
		{#each services as service, i (service.name)}
			<div 
				class="bg-gradient-to-br from-slate-900 to-slate-800 rounded-xl border {getStatusBg(service.status)} p-5 shadow-lg hover:shadow-xl transition-shadow"
				in:scale={{ delay: i * 50, duration: 200 }}
			>
				<div class="flex items-start justify-between mb-3">
					<div class="flex items-center gap-3">
						<span class="text-2xl">{service.icon}</span>
						<div>
							<h3 class="font-medium text-white">{service.displayName}</h3>
							<p class="text-xs text-slate-400 capitalize">{service.status}</p>
						</div>
					</div>
					<div class="w-3 h-3 rounded-full bg-gradient-to-br {getStatusColor(service.status)} shadow-lg {service.status === 'checking' ? 'animate-pulse' : ''}"></div>
				</div>
				
				{#if service.message}
					<p class="text-sm text-slate-400 truncate">{service.message}</p>
				{/if}
				
				{#if service.latency}
					<p class="text-xs text-slate-500 mt-2">Latency: {service.latency}ms</p>
				{/if}
				
				<p class="text-xs text-slate-600 mt-2">
					Checked: {service.lastCheck.toLocaleTimeString()}
				</p>
			</div>
		{/each}
	</div>
	
	<!-- Data Flow Visualization -->
	<div class="bg-gradient-to-br from-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 p-6 shadow-xl">
		<h2 class="text-lg font-medium text-white mb-6 flex items-center gap-2">
			<span>🌊</span> Data Flow Pipeline
		</h2>
		
		<div class="flex items-center justify-between gap-2 overflow-x-auto pb-2">
			{#each dataFlow as node, i (node.id)}
				<div class="flex items-center gap-2 flex-shrink-0">
					<div 
						class="px-4 py-3 rounded-xl border {getFlowColor(node.status)} min-w-[100px] text-center transition-all"
						in:fly={{ x: -20, delay: i * 100, duration: 300 }}
					>
						<div class="text-xl mb-1">{node.icon}</div>
						<div class="text-xs font-medium">{node.label}</div>
						{#if node.throughput}
							<div class="text-xs opacity-60 mt-1">{node.throughput}/s</div>
						{/if}
					</div>
					
					{#if i < dataFlow.length - 1}
						<div class="text-slate-600 text-xl animate-pulse">→</div>
					{/if}
				</div>
			{/each}
		</div>
		
		<div class="mt-4 pt-4 border-t border-slate-700/50 flex items-center gap-6 text-sm">
			<div class="flex items-center gap-2">
				<div class="w-3 h-3 rounded-full bg-emerald-500"></div>
				<span class="text-slate-400">Active</span>
			</div>
			<div class="flex items-center gap-2">
				<div class="w-3 h-3 rounded-full bg-slate-500"></div>
				<span class="text-slate-400">Idle</span>
			</div>
			<div class="flex items-center gap-2">
				<div class="w-3 h-3 rounded-full bg-red-500"></div>
				<span class="text-slate-400">Error</span>
			</div>
		</div>
	</div>
	
	<!-- Quick Actions -->
	<div class="grid grid-cols-1 md:grid-cols-3 gap-4">
		<a 
			href="/coral"
			class="bg-gradient-to-br from-coral-900/20 to-slate-900 hover:from-coral-900/30 rounded-xl border border-coral-800/30 p-5 transition-all group"
		>
			<div class="flex items-center gap-3 mb-2">
				<span class="text-2xl">🐚</span>
				<h3 class="font-medium text-coral-300">Ask Coral</h3>
			</div>
			<p class="text-sm text-slate-400">Get intelligent help understanding your system status</p>
		</a>
		
		<a 
			href="http://localhost:9000"
			target="_blank"
			class="bg-slate-900 hover:bg-slate-800 rounded-xl border border-slate-700/50 p-5 transition-all group"
		>
			<div class="flex items-center gap-3 mb-2">
				<span class="text-2xl">⏱️</span>
				<h3 class="font-medium text-white">QuestDB Console</h3>
				<span class="text-xs text-slate-600">↗</span>
			</div>
			<p class="text-sm text-slate-400">Direct access to time-series database queries</p>
		</a>
		
		<a 
			href="/sql"
			class="bg-slate-900 hover:bg-slate-800 rounded-xl border border-slate-700/50 p-5 transition-all group"
		>
			<div class="flex items-center gap-3 mb-2">
				<span class="text-2xl">🐘</span>
				<h3 class="font-medium text-white">PostgreSQL Console</h3>
			</div>
			<p class="text-sm text-slate-400">Query metadata and configuration</p>
		</a>
	</div>
</div>

<style>
	.bg-coral-900\/20 { background-color: rgba(134, 51, 51, 0.2); }
	.hover\:from-coral-900\/30:hover { --tw-gradient-from: rgba(134, 51, 51, 0.3); }
	.border-coral-800\/30 { border-color: rgba(163, 57, 57, 0.3); }
	.text-coral-300 { color: #ffa3a3; }
</style>
