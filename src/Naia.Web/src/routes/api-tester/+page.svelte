<script lang="ts">
	import { onMount } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	
	interface ApiPreset {
		name: string;
		method: 'GET' | 'POST' | 'PUT' | 'DELETE';
		url: string;
		body?: string;
		description: string;
	}
	
	interface RequestHistory {
		id: string;
		method: string;
		url: string;
		status: number;
		duration: number;
		timestamp: Date;
	}
	
	let method = $state<'GET' | 'POST' | 'PUT' | 'DELETE'>('GET');
	let url = $state('/api/health');
	let requestBody = $state('');
	let headers = $state<{key: string, value: string}[]>([
		{ key: 'Content-Type', value: 'application/json' }
	]);
	
	let response = $state<{status: number, statusText: string, body: string, duration: number} | null>(null);
	let isLoading = $state(false);
	let error = $state<string | null>(null);
	let history = $state<RequestHistory[]>([]);
	
	const presets: ApiPreset[] = [
		{ name: 'Health Check', method: 'GET', url: '/api/health', description: 'Check API and database health' },
		{ name: 'Pipeline Health', method: 'GET', url: '/api/pipeline/health', description: 'Check ingestion pipeline status' },
		{ name: 'List Points', method: 'GET', url: '/api/points?limit=20', description: 'Get first 20 points' },
		{ name: 'Search Points', method: 'GET', url: '/api/points/search?q=power', description: 'Search points by name' },
		{ name: 'List Equipment', method: 'GET', url: '/api/equipment', description: 'Get all equipment' },
		{ name: 'List Data Sources', method: 'GET', url: '/api/datasources', description: 'Get configured data sources' },
		{ name: 'Recent Data', method: 'POST', url: '/api/data/query', body: JSON.stringify({ pointIds: [1], hours: 1 }, null, 2), description: 'Query recent time-series' },
		{ name: 'Point Stats', method: 'GET', url: '/api/stats/point/1?hours=24', description: 'Get statistics for point' },
		{ name: 'Patterns List', method: 'GET', url: '/api/patterns', description: 'List all patterns' },
		{ name: 'System Config', method: 'GET', url: '/api/config', description: 'Get system configuration' },
	];
	
	function loadPreset(preset: ApiPreset) {
		method = preset.method;
		url = preset.url;
		requestBody = preset.body || '';
	}
	
	async function sendRequest() {
		isLoading = true;
		error = null;
		response = null;
		
		const start = performance.now();
		
		try {
			const fetchOptions: RequestInit = {
				method,
				headers: headers.reduce((acc, h) => {
					if (h.key && h.value) acc[h.key] = h.value;
					return acc;
				}, {} as Record<string, string>)
			};
			
			if (['POST', 'PUT'].includes(method) && requestBody) {
				fetchOptions.body = requestBody;
			}
			
			const res = await fetch(url, fetchOptions);
			const duration = Math.round(performance.now() - start);
			
			let body: string;
			const contentType = res.headers.get('Content-Type') || '';
			if (contentType.includes('application/json')) {
				const json = await res.json();
				body = JSON.stringify(json, null, 2);
			} else {
				body = await res.text();
			}
			
			response = {
				status: res.status,
				statusText: res.statusText,
				body,
				duration
			};
			
			// Add to history
			history = [{
				id: crypto.randomUUID(),
				method,
				url,
				status: res.status,
				duration,
				timestamp: new Date()
			}, ...history.slice(0, 19)];
			
		} catch (e: any) {
			error = e.message || 'Request failed';
		} finally {
			isLoading = false;
		}
	}
	
	function addHeader() {
		headers = [...headers, { key: '', value: '' }];
	}
	
	function removeHeader(index: number) {
		headers = headers.filter((_, i) => i !== index);
	}
	
	function getStatusColor(status: number): string {
		if (status >= 200 && status < 300) return 'text-emerald-400';
		if (status >= 300 && status < 400) return 'text-blue-400';
		if (status >= 400 && status < 500) return 'text-amber-400';
		return 'text-red-400';
	}
	
	function getMethodColor(method: string): string {
		switch (method) {
			case 'GET': return 'bg-emerald-500/20 text-emerald-400';
			case 'POST': return 'bg-blue-500/20 text-blue-400';
			case 'PUT': return 'bg-amber-500/20 text-amber-400';
			case 'DELETE': return 'bg-red-500/20 text-red-400';
			default: return 'bg-slate-500/20 text-slate-400';
		}
	}
	
	function handleKeydown(e: KeyboardEvent) {
		if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
			sendRequest();
		}
	}
</script>

<svelte:head>
	<title>API Tester | NAIA</title>
</svelte:head>

<div class="space-y-6" onkeydown={handleKeydown}>
	<!-- Header -->
	<div>
		<h1 class="text-2xl font-semibold text-white flex items-center gap-3">
			<span class="text-3xl">🧪</span>
			API Tester
		</h1>
		<p class="text-slate-400 mt-1">Test NAIA API endpoints with presets (like Postman)</p>
	</div>
	
	<div class="grid grid-cols-1 lg:grid-cols-4 gap-6">
		<!-- Presets -->
		<div class="lg:col-span-1 space-y-4">
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
					<h2 class="font-medium text-white">Quick Presets</h2>
				</div>
				<div class="max-h-[500px] overflow-y-auto">
					{#each presets as preset}
						<button
							onclick={() => loadPreset(preset)}
							class="w-full px-4 py-3 text-left hover:bg-slate-800/50 border-b border-slate-700/30 transition-colors"
						>
							<div class="flex items-center gap-2">
								<span class="px-2 py-0.5 rounded text-xs font-mono {getMethodColor(preset.method)}">
									{preset.method}
								</span>
								<span class="text-sm text-white">{preset.name}</span>
							</div>
							<p class="text-xs text-slate-500 mt-1">{preset.description}</p>
						</button>
					{/each}
				</div>
			</div>
			
			<!-- History -->
			{#if history.length > 0}
				<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
					<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
						<h2 class="font-medium text-white">Recent</h2>
					</div>
					<div class="max-h-60 overflow-y-auto">
						{#each history as req (req.id)}
							<button
								onclick={() => { url = req.url; method = req.method as any; }}
								class="w-full px-4 py-2 text-left hover:bg-slate-800/50 border-b border-slate-700/30 transition-colors"
							>
								<div class="flex items-center gap-2">
									<span class="px-1.5 py-0.5 rounded text-xs font-mono {getMethodColor(req.method)}">
										{req.method}
									</span>
									<span class="{getStatusColor(req.status)} text-xs">{req.status}</span>
									<span class="text-xs text-slate-600">{req.duration}ms</span>
								</div>
								<p class="text-xs text-slate-500 truncate mt-1">{req.url}</p>
							</button>
						{/each}
					</div>
				</div>
			{/if}
		</div>
		
		<!-- Request Builder -->
		<div class="lg:col-span-3 space-y-4">
			<!-- URL Bar -->
			<div class="flex gap-2">
				<select 
					bind:value={method}
					class="px-4 py-3 bg-slate-800 border border-slate-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:ring-2 focus:ring-teal-500"
				>
					<option value="GET">GET</option>
					<option value="POST">POST</option>
					<option value="PUT">PUT</option>
					<option value="DELETE">DELETE</option>
				</select>
				<input
					type="text"
					bind:value={url}
					placeholder="Enter request URL"
					class="flex-1 px-4 py-3 bg-slate-800 border border-slate-700 rounded-lg text-white font-mono text-sm placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-teal-500"
				/>
				<button
					onclick={sendRequest}
					disabled={isLoading || !url}
					class="px-6 py-3 bg-teal-600 hover:bg-teal-500 disabled:bg-slate-700 text-white font-medium rounded-lg transition-colors flex items-center gap-2"
				>
					{#if isLoading}
						<span class="animate-spin">⏳</span>
					{:else}
						<span>🚀</span>
					{/if}
					Send
				</button>
			</div>
			
			<p class="text-xs text-slate-500">Press Ctrl+Enter to send request</p>
			
			<!-- Tabs for Headers/Body -->
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="flex border-b border-slate-700/50">
					<button class="px-4 py-2 text-sm {['POST', 'PUT'].includes(method) ? 'text-white border-b-2 border-teal-500' : 'text-slate-400'}">
						Body
					</button>
					<button class="px-4 py-2 text-sm text-slate-400">
						Headers ({headers.filter(h => h.key).length})
					</button>
				</div>
				
				{#if ['POST', 'PUT'].includes(method)}
					<div class="p-4">
						<textarea
							bind:value={requestBody}
							placeholder={"{ \"key\": \"value\" }"}
							rows="8"
							class="w-full px-4 py-3 bg-slate-800 border border-slate-700 rounded-lg text-white font-mono text-sm placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-teal-500 resize-y"
						></textarea>
					</div>
				{:else}
					<div class="p-4 text-sm text-slate-500">
						Body is available for POST and PUT requests
					</div>
				{/if}
			</div>
			
			<!-- Response -->
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50 flex items-center justify-between">
					<h2 class="font-medium text-white">Response</h2>
					{#if response}
						<div class="flex items-center gap-4 text-sm">
							<span class="{getStatusColor(response.status)} font-mono">
								{response.status} {response.statusText}
							</span>
							<span class="text-slate-500">{response.duration}ms</span>
						</div>
					{/if}
				</div>
				
				<div class="p-4 max-h-96 overflow-auto">
					{#if error}
						<div class="text-red-400" in:fade>
							Error: {error}
						</div>
					{:else if response}
						<pre class="text-sm text-slate-300 font-mono whitespace-pre-wrap" in:fade>{response.body}</pre>
					{:else if isLoading}
						<div class="text-slate-500 flex items-center gap-2">
							<span class="animate-spin">⏳</span>
							Sending request...
						</div>
					{:else}
						<div class="text-slate-500">
							Select a preset or enter a URL and click Send
						</div>
					{/if}
				</div>
			</div>
		</div>
	</div>
</div>
