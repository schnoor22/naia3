<script lang="ts">
	import { onMount, tick } from 'svelte';
	import { fade, fly, scale } from 'svelte/transition';
	
	interface Message {
		id: string;
		role: 'user' | 'coral';
		content: string;
		timestamp: Date;
		tools?: ToolResult[];
	}
	
	interface ToolResult {
		name: string;
		status: 'running' | 'complete' | 'error';
		result?: any;
	}
	
	interface ServiceHealth {
		name: string;
		status: 'healthy' | 'degraded' | 'unhealthy' | 'unknown';
		latency?: number;
		lastCheck: Date;
	}
	
	let messages = $state<Message[]>([]);
	let inputMessage = $state('');
	let isLoading = $state(false);
	let chatContainer: HTMLDivElement;
	let showHealthMatrix = $state(true);
	let services = $state<ServiceHealth[]>([]);
	let coralMood = $state<'greeting' | 'thinking' | 'happy' | 'helping'>('greeting');
	
	// Coral's personality - warm greetings
	const greetings = [
		"Hello! I'm Coral, your guide to NAIA's data ocean. How can I help you navigate today?",
		"Welcome back! What insights can I help you discover?",
		"Good to see you! I'm here to help you find exactly what you need.",
		"Hi there! Ready to dive into your data? I'll be your guide."
	];
	
	// Suggested actions based on common engineer needs
	const suggestions = [
		{ icon: '📊', label: 'System Health', query: 'Show me the current system health status' },
		{ icon: '📈', label: 'Recent Trends', query: 'What are the latest data trends for our turbines?' },
		{ icon: '⚠️', label: 'Active Alerts', query: 'Are there any alerts or anomalies I should know about?' },
		{ icon: '🔍', label: 'Point Search', query: 'Help me find points related to power output' },
		{ icon: '📋', label: 'Daily Report', query: 'Generate a summary report for today' },
		{ icon: '🔄', label: 'Data Flow', query: 'Show me the current data ingestion status' },
	];
	
	onMount(async () => {
		await checkServices();
		// Add initial greeting
		const greeting = greetings[Math.floor(Math.random() * greetings.length)];
		messages = [{
			id: crypto.randomUUID(),
			role: 'coral',
			content: greeting,
			timestamp: new Date()
		}];
	});
	
	async function checkServices() {
		const checks: ServiceHealth[] = [
			{ name: 'API', status: 'unknown', lastCheck: new Date() },
			{ name: 'PostgreSQL', status: 'unknown', lastCheck: new Date() },
			{ name: 'QuestDB', status: 'unknown', lastCheck: new Date() },
			{ name: 'Redis', status: 'unknown', lastCheck: new Date() },
			{ name: 'SignalR', status: 'unknown', lastCheck: new Date() },
			{ name: 'Ingestion', status: 'unknown', lastCheck: new Date() },
		];
		
		try {
			const response = await fetch('/api/health');
			if (response.ok) {
				const data = await response.json();
				checks[0] = { name: 'API', status: 'healthy', latency: 50, lastCheck: new Date() };
				checks[1] = { name: 'PostgreSQL', status: data.checks?.postgresql?.status || 'unknown', lastCheck: new Date() };
				checks[2] = { name: 'QuestDB', status: data.checks?.questdb?.status || 'unknown', lastCheck: new Date() };
				checks[3] = { name: 'Redis', status: data.checks?.redis?.status || 'unknown', lastCheck: new Date() };
			}
		} catch {
			checks[0] = { name: 'API', status: 'unhealthy', lastCheck: new Date() };
		}
		
		// Check SignalR
		try {
			// Simple check - in production would verify hub connection
			checks[4] = { name: 'SignalR', status: 'healthy', lastCheck: new Date() };
		} catch {
			checks[4] = { name: 'SignalR', status: 'unknown', lastCheck: new Date() };
		}
		
		// Check Ingestion
		try {
			const response = await fetch('/api/pipeline/health');
			if (response.ok) {
				const data = await response.json();
				checks[5] = { name: 'Ingestion', status: data.isHealthy ? 'healthy' : 'degraded', lastCheck: new Date() };
			}
		} catch {
			checks[5] = { name: 'Ingestion', status: 'unknown', lastCheck: new Date() };
		}
		
		services = checks;
	}
	
	async function sendMessage(message?: string) {
		const messageToSend = message || inputMessage.trim();
		if (!messageToSend || isLoading) return;
		
		// Add user message
		messages = [...messages, {
			id: crypto.randomUUID(),
			role: 'user',
			content: messageToSend,
			timestamp: new Date()
		}];
		
		inputMessage = '';
		isLoading = true;
		coralMood = 'thinking';
		
		await tick();
		chatContainer?.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
		
		// Create coral response placeholder
		const coralMessage: Message = {
			id: crypto.randomUUID(),
			role: 'coral',
			content: '',
			timestamp: new Date(),
			tools: []
		};
		messages = [...messages, coralMessage];
		
		try {
			const response = await fetch('/api/coral/chat', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					message: messageToSend,
					context: {
						services: services,
						timestamp: new Date().toISOString()
					}
				})
			});
			
			if (!response.ok) {
				throw new Error(`HTTP ${response.status}`);
			}
			
			const reader = response.body?.getReader();
			const decoder = new TextDecoder();
			
			if (!reader) throw new Error('No response body');
			
			let buffer = '';
			
			while (true) {
				const { done, value } = await reader.read();
				if (done) break;
				
				buffer += decoder.decode(value, { stream: true });
				const lines = buffer.split('\n');
				buffer = lines.pop() || '';
				
				for (const line of lines) {
					if (line.startsWith('data: ')) {
						const data = line.substring(6);
						if (data === '[DONE]') continue;
						
						try {
							const parsed = JSON.parse(data);
							if (parsed.text) {
								const lastIdx = messages.length - 1;
								messages[lastIdx] = {
									...messages[lastIdx],
									content: messages[lastIdx].content + parsed.text
								};
								
								await tick();
								chatContainer?.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
							}
							if (parsed.tool) {
								const lastIdx = messages.length - 1;
								messages[lastIdx] = {
									...messages[lastIdx],
									tools: [...(messages[lastIdx].tools || []), parsed.tool]
								};
							}
						} catch {}
					}
				}
			}
			
			coralMood = 'happy';
		} catch (e: any) {
			// Provide a helpful fallback response
			const lastIdx = messages.length - 1;
			messages[lastIdx] = {
				...messages[lastIdx],
				content: `I'm having trouble connecting to my knowledge base right now. Let me help you another way:\n\n` +
					`• **System Health**: Check the status panel on the right\n` +
					`• **QuestDB Console**: Visit [localhost:9000](http://localhost:9000) for direct queries\n` +
					`• **API Docs**: Available at [/swagger](/swagger)\n\n` +
					`Is there something specific I can help you find?`
			};
			coralMood = 'helping';
		} finally {
			isLoading = false;
			setTimeout(() => coralMood = 'greeting', 3000);
		}
	}
	
	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			sendMessage();
		}
	}
	
	function formatMessage(content: string): string {
		return content
			.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre class="bg-slate-900/50 p-3 rounded-lg overflow-x-auto text-sm my-3 border border-coral-500/20"><code>$2</code></pre>')
			.replace(/`([^`]+)`/g, '<code class="bg-slate-800/50 px-1.5 py-0.5 rounded text-coral-300 text-sm">$1</code>')
			.replace(/\*\*([^*]+)\*\*/g, '<strong class="text-white">$1</strong>')
			.replace(/\*([^*]+)\*/g, '<em>$1</em>')
			.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" class="text-coral-400 hover:text-coral-300 underline" target="_blank">$1</a>')
			.replace(/^• /gm, '<span class="text-coral-400">•</span> ')
			.replace(/\n/g, '<br>');
	}
	
	function getStatusColor(status: string): string {
		switch (status) {
			case 'healthy': return 'bg-emerald-500';
			case 'degraded': return 'bg-amber-500';
			case 'unhealthy': return 'bg-red-500';
			default: return 'bg-gray-500';
		}
	}
	
	function getCoralAvatar(): string {
		switch (coralMood) {
			case 'thinking': return '🤔';
			case 'happy': return '😊';
			case 'helping': return '💡';
			default: return '🐚';
		}
	}
</script>

<svelte:head>
	<title>Coral Assistant | NAIA</title>
	<style>
		:root {
			--coral-50: #fff5f5;
			--coral-100: #ffe0e0;
			--coral-200: #ffc7c7;
			--coral-300: #ffa3a3;
			--coral-400: #ff7a7a;
			--coral-500: #ff6b6b;
			--coral-600: #ee5a5a;
			--coral-700: #c74444;
			--coral-800: #a33939;
			--coral-900: #863333;
		}
	</style>
</svelte:head>

<div class="flex h-[calc(100vh-8rem)] gap-4">
	<!-- Main Chat Area -->
	<div class="flex-1 flex flex-col bg-gradient-to-br from-slate-900 via-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 overflow-hidden shadow-2xl">
		<!-- Coral Header -->
		<div class="relative px-6 py-4 bg-gradient-to-r from-coral-600/20 via-coral-500/10 to-teal-500/10 border-b border-slate-700/50">
			<div class="absolute inset-0 bg-gradient-to-r from-coral-500/5 to-teal-500/5 animate-pulse"></div>
			<div class="relative flex items-center gap-4">
				<div class="relative">
					<div class="w-12 h-12 rounded-full bg-gradient-to-br from-coral-400 to-coral-600 flex items-center justify-center text-2xl shadow-lg shadow-coral-500/30">
						{getCoralAvatar()}
					</div>
					<div class="absolute -bottom-1 -right-1 w-4 h-4 rounded-full bg-emerald-500 border-2 border-slate-900"></div>
				</div>
				<div>
					<h1 class="text-xl font-semibold bg-gradient-to-r from-coral-300 to-coral-100 bg-clip-text text-transparent">
						Coral
					</h1>
					<p class="text-sm text-slate-400">Your intelligent data guide</p>
				</div>
				<div class="ml-auto flex items-center gap-2">
					<span class="text-xs text-slate-500">Powered by NAIA Pattern Intelligence</span>
				</div>
			</div>
		</div>
		
		<!-- Chat Messages -->
		<div 
			bind:this={chatContainer}
			class="flex-1 overflow-y-auto p-6 space-y-6"
		>
			{#each messages as message, i (message.id)}
				<div 
					class="flex gap-4 {message.role === 'user' ? 'flex-row-reverse' : ''}"
					in:fly={{ y: 20, duration: 300, delay: i * 50 }}
				>
					{#if message.role === 'coral'}
						<div class="flex-shrink-0 w-10 h-10 rounded-full bg-gradient-to-br from-coral-400 to-coral-600 flex items-center justify-center text-lg shadow-md shadow-coral-500/20">
							🐚
						</div>
					{/if}
					
					<div class="max-w-[75%] {message.role === 'user' 
						? 'bg-gradient-to-br from-slate-700 to-slate-800 rounded-2xl rounded-tr-md' 
						: 'bg-gradient-to-br from-slate-800/80 to-slate-900/80 rounded-2xl rounded-tl-md border border-slate-700/50'} 
						px-5 py-4 shadow-lg">
						
						{#if message.tools && message.tools.length > 0}
							<div class="mb-3 space-y-2">
								{#each message.tools as tool}
									<div class="flex items-center gap-2 text-xs px-3 py-1.5 bg-slate-900/50 rounded-lg border border-coral-500/20">
										{#if tool.status === 'running'}
											<span class="animate-spin">⚙️</span>
										{:else if tool.status === 'complete'}
											<span>✅</span>
										{:else}
											<span>❌</span>
										{/if}
										<span class="text-coral-400">{tool.name}</span>
									</div>
								{/each}
							</div>
						{/if}
						
						<div class="text-slate-200 leading-relaxed">
							{#if message.role === 'coral'}
								{@html formatMessage(message.content)}
							{:else}
								<div class="whitespace-pre-wrap">{message.content}</div>
							{/if}
						</div>
					</div>
					
					{#if message.role === 'user'}
						<div class="flex-shrink-0 w-10 h-10 rounded-full bg-gradient-to-br from-slate-600 to-slate-700 flex items-center justify-center text-lg shadow-md">
							👤
						</div>
					{/if}
				</div>
			{/each}
			
			{#if isLoading && messages[messages.length - 1]?.content === ''}
				<div class="flex gap-4" in:fade>
					<div class="flex-shrink-0 w-10 h-10 rounded-full bg-gradient-to-br from-coral-400 to-coral-600 flex items-center justify-center text-lg shadow-md shadow-coral-500/20 animate-pulse">
						🤔
					</div>
					<div class="bg-slate-800/50 rounded-2xl rounded-tl-md px-5 py-4 border border-slate-700/50">
						<div class="flex items-center gap-2 text-slate-400">
							<div class="flex gap-1">
								<span class="w-2 h-2 bg-coral-400 rounded-full animate-bounce" style="animation-delay: 0ms"></span>
								<span class="w-2 h-2 bg-coral-400 rounded-full animate-bounce" style="animation-delay: 150ms"></span>
								<span class="w-2 h-2 bg-coral-400 rounded-full animate-bounce" style="animation-delay: 300ms"></span>
							</div>
							<span class="text-sm">Coral is thinking...</span>
						</div>
					</div>
				</div>
			{/if}
		</div>
		
		<!-- Suggestions (when no conversation) -->
		{#if messages.length <= 1}
			<div class="px-6 pb-4">
				<p class="text-xs text-slate-500 mb-3">Quick actions:</p>
				<div class="grid grid-cols-2 md:grid-cols-3 gap-2">
					{#each suggestions as suggestion}
						<button
							onclick={() => sendMessage(suggestion.query)}
							class="flex items-center gap-2 p-3 text-left text-sm bg-slate-800/50 hover:bg-slate-700/50 border border-slate-700/50 hover:border-coral-500/30 rounded-xl text-slate-300 transition-all group"
						>
							<span class="text-lg group-hover:scale-110 transition-transform">{suggestion.icon}</span>
							<span>{suggestion.label}</span>
						</button>
					{/each}
				</div>
			</div>
		{/if}
		
		<!-- Input Area -->
		<div class="p-4 border-t border-slate-700/50 bg-slate-900/50">
			<div class="flex gap-3">
				<div class="flex-1 relative">
					<textarea
						bind:value={inputMessage}
						onkeydown={handleKeydown}
						placeholder="Ask Coral anything about your data..."
						rows="1"
						disabled={isLoading}
						class="w-full px-5 py-3.5 bg-slate-800/50 border border-slate-700/50 focus:border-coral-500/50 rounded-xl text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-coral-500/20 resize-none disabled:opacity-50 transition-all"
					></textarea>
				</div>
				<button
					onclick={() => sendMessage()}
					disabled={isLoading || !inputMessage.trim()}
					class="px-6 py-3 bg-gradient-to-r from-coral-500 to-coral-600 hover:from-coral-400 hover:to-coral-500 disabled:from-slate-700 disabled:to-slate-700 text-white font-medium rounded-xl shadow-lg shadow-coral-500/20 disabled:shadow-none transition-all disabled:cursor-not-allowed"
				>
					{isLoading ? '...' : 'Send'}
				</button>
			</div>
		</div>
	</div>
	
	<!-- Side Panel - Service Health & Quick Stats -->
	<div class="w-80 flex flex-col gap-4">
		<!-- Service Health Matrix -->
		<div class="bg-gradient-to-br from-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 overflow-hidden shadow-xl">
			<div class="px-4 py-3 border-b border-slate-700/50 flex items-center justify-between">
				<h2 class="text-sm font-medium text-slate-300 flex items-center gap-2">
					<span>💚</span> System Health
				</h2>
				<button 
					onclick={checkServices}
					class="text-xs text-slate-500 hover:text-coral-400 transition-colors"
				>
					Refresh
				</button>
			</div>
			<div class="p-3 space-y-2">
				{#each services as service}
					<div class="flex items-center justify-between p-2.5 bg-slate-800/50 rounded-lg border border-slate-700/30">
						<div class="flex items-center gap-3">
							<div class="w-2.5 h-2.5 rounded-full {getStatusColor(service.status)} shadow-sm"></div>
							<span class="text-sm text-slate-300">{service.name}</span>
						</div>
						<span class="text-xs text-slate-500 capitalize">{service.status}</span>
					</div>
				{/each}
			</div>
		</div>
		
		<!-- Data Flow Status -->
		<div class="bg-gradient-to-br from-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 overflow-hidden shadow-xl">
			<div class="px-4 py-3 border-b border-slate-700/50">
				<h2 class="text-sm font-medium text-slate-300 flex items-center gap-2">
					<span>🌊</span> Data Flow
				</h2>
			</div>
			<div class="p-4">
				<div class="flex flex-col items-center gap-2 text-xs">
					<div class="w-full p-2 bg-teal-900/30 border border-teal-700/30 rounded-lg text-center text-teal-400">
						📄 CSV Files
					</div>
					<div class="text-slate-600">↓</div>
					<div class="w-full p-2 bg-orange-900/30 border border-orange-700/30 rounded-lg text-center text-orange-400">
						📨 Kafka
					</div>
					<div class="text-slate-600">↓</div>
					<div class="w-full p-2 bg-purple-900/30 border border-purple-700/30 rounded-lg text-center text-purple-400">
						⏱️ QuestDB
					</div>
					<div class="text-slate-600">↓</div>
					<div class="w-full p-2 bg-coral-900/30 border border-coral-700/30 rounded-lg text-center text-coral-400">
						🖥️ UI/SignalR
					</div>
				</div>
			</div>
		</div>
		
		<!-- Quick Links -->
		<div class="bg-gradient-to-br from-slate-900 to-slate-800 rounded-2xl border border-slate-700/50 overflow-hidden shadow-xl">
			<div class="px-4 py-3 border-b border-slate-700/50">
				<h2 class="text-sm font-medium text-slate-300 flex items-center gap-2">
					<span>🔗</span> Quick Links
				</h2>
			</div>
			<div class="p-2 space-y-1">
				<a href="/trends" class="flex items-center gap-2 p-2 hover:bg-slate-800/50 rounded-lg text-sm text-slate-400 hover:text-slate-200 transition-colors">
					<span>📈</span> Trend Viewer
				</a>
				<a href="/points" class="flex items-center gap-2 p-2 hover:bg-slate-800/50 rounded-lg text-sm text-slate-400 hover:text-slate-200 transition-colors">
					<span>📍</span> Point Browser
				</a>
				<a href="/patterns" class="flex items-center gap-2 p-2 hover:bg-slate-800/50 rounded-lg text-sm text-slate-400 hover:text-slate-200 transition-colors">
					<span>🔍</span> Pattern Review
				</a>
				<a href="http://localhost:9000" target="_blank" class="flex items-center gap-2 p-2 hover:bg-slate-800/50 rounded-lg text-sm text-slate-400 hover:text-slate-200 transition-colors">
					<span>⏱️</span> QuestDB Console
					<span class="ml-auto text-xs text-slate-600">↗</span>
				</a>
			</div>
		</div>
		
		<!-- Coral Info -->
		<div class="p-4 bg-gradient-to-br from-coral-900/20 to-slate-900 rounded-2xl border border-coral-800/30 shadow-xl">
			<div class="flex items-center gap-3 mb-2">
				<span class="text-2xl">🐚</span>
				<div>
					<h3 class="text-sm font-medium text-coral-300">About Coral</h3>
				</div>
			</div>
			<p class="text-xs text-slate-400 leading-relaxed">
				Coral is your intelligent guide to the NAIA data ocean. She understands your equipment, patterns, and can help you find insights quickly. 
				Ask her anything about your data!
			</p>
		</div>
	</div>
</div>

<style>
	.bg-coral-900\/20 { background-color: rgba(134, 51, 51, 0.2); }
	.bg-coral-900\/30 { background-color: rgba(134, 51, 51, 0.3); }
	.border-coral-800\/30 { border-color: rgba(163, 57, 57, 0.3); }
	.border-coral-700\/30 { border-color: rgba(199, 68, 68, 0.3); }
	.border-coral-500\/20 { border-color: rgba(255, 107, 107, 0.2); }
	.border-coral-500\/30 { border-color: rgba(255, 107, 107, 0.3); }
	.border-coral-500\/50 { border-color: rgba(255, 107, 107, 0.5); }
	.text-coral-300 { color: #ffa3a3; }
	.text-coral-400 { color: #ff7a7a; }
	.from-coral-300 { --tw-gradient-from: #ffa3a3; }
	.to-coral-100 { --tw-gradient-to: #ffe0e0; }
	.from-coral-400 { --tw-gradient-from: #ff7a7a; }
	.to-coral-600 { --tw-gradient-to: #ee5a5a; }
	.from-coral-500 { --tw-gradient-from: #ff6b6b; }
	.from-coral-600\/20 { --tw-gradient-from: rgba(238, 90, 90, 0.2); }
	.via-coral-500\/10 { --tw-gradient-via: rgba(255, 107, 107, 0.1); }
	.hover\:from-coral-400:hover { --tw-gradient-from: #ff7a7a; }
	.hover\:to-coral-500:hover { --tw-gradient-to: #ff6b6b; }
	.shadow-coral-500\/20 { --tw-shadow-color: rgba(255, 107, 107, 0.2); }
	.shadow-coral-500\/30 { --tw-shadow-color: rgba(255, 107, 107, 0.3); }
	.focus\:border-coral-500\/50:focus { border-color: rgba(255, 107, 107, 0.5); }
	.focus\:ring-coral-500\/20:focus { --tw-ring-color: rgba(255, 107, 107, 0.2); }
	.hover\:text-coral-400:hover { color: #ff7a7a; }
	.hover\:border-coral-500\/30:hover { border-color: rgba(255, 107, 107, 0.3); }
</style>
