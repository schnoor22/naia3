<script lang="ts">
	import { onMount, tick } from 'svelte';
	import { isMasterMode, masterToken, loginMaster, getMasterHeaders } from '$lib/stores/master';
	
	interface Message {
		role: 'user' | 'assistant';
		content: string;
		timestamp: Date;
	}
	
	let messages = $state<Message[]>([]);
	let inputMessage = $state('');
	let isLoading = $state(false);
	let chatContainer: HTMLDivElement;
	let masterTokenInput = $state('');
	let loginError = $state<string | null>(null);
	let context = $state<any>(null);
	let showContext = $state(false);
	
	// Suggested queries
	const suggestedQueries = [
		"What's the current system status?",
		"Show me recent ingestion logs",
		"How many points were ingested in the last hour?",
		"Explain how the pattern engine works",
		"What files handle CSV ingestion?",
		"Show me the data flow architecture"
	];
	
	onMount(async () => {
		// Load context if in master mode
		if ($isMasterMode) {
			await loadContext();
		}
	});
	
	async function loadContext() {
		try {
			const response = await fetch('/api/debug/context', {
				headers: getMasterHeaders()
			});
			if (response.ok) {
				context = await response.json();
			}
		} catch (e) {
			console.error('Failed to load context:', e);
		}
	}
	
	async function handleLogin() {
		loginError = null;
		const result = await loginMaster(masterTokenInput);
		if (result.success) {
			masterTokenInput = '';
			await loadContext();
		} else {
			loginError = result.error || 'Login failed';
		}
	}
	
	async function sendMessage(message?: string) {
		const messageToSend = message || inputMessage.trim();
		if (!messageToSend || isLoading) return;
		
		// Add user message
		messages = [...messages, {
			role: 'user',
			content: messageToSend,
			timestamp: new Date()
		}];
		
		inputMessage = '';
		isLoading = true;
		
		// Scroll to bottom
		await tick();
		chatContainer?.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
		
		// Create assistant message placeholder
		const assistantMessage: Message = {
			role: 'assistant',
			content: '',
			timestamp: new Date()
		};
		messages = [...messages, assistantMessage];
		
		try {
			// Build history for context
			const history = messages.slice(0, -1).map(m => ({
				role: m.role,
				content: m.content
			}));
			
			const response = await fetch('/api/debug/chat', {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					...getMasterHeaders()
				},
				body: JSON.stringify({
					message: messageToSend,
					history: history.slice(-10) // Last 10 messages for context
				})
			});
			
			if (!response.ok) {
				throw new Error(`HTTP ${response.status}: ${await response.text()}`);
			}
			
			// Read SSE stream
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
								// Update the last message (assistant)
								const lastIdx = messages.length - 1;
								messages[lastIdx] = {
									...messages[lastIdx],
									content: messages[lastIdx].content + parsed.text
								};
								
								// Scroll as content streams
								await tick();
								chatContainer?.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
							}
							if (parsed.error) {
								messages[messages.length - 1].content += `\n\n⚠️ Error: ${parsed.error}`;
							}
						} catch (e) {
							// Ignore parse errors for incomplete chunks
						}
					}
				}
			}
		} catch (e: any) {
			console.error('Chat error:', e);
			messages[messages.length - 1].content = `❌ Error: ${e.message}`;
		} finally {
			isLoading = false;
			await tick();
			chatContainer?.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
		}
	}
	
	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			sendMessage();
		}
	}
	
	function clearChat() {
		messages = [];
	}
	
	function formatMessage(content: string): string {
		// Basic markdown-like formatting
		return content
			.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre class="bg-gray-900 p-3 rounded-lg overflow-x-auto text-sm my-2"><code>$2</code></pre>')
			.replace(/`([^`]+)`/g, '<code class="bg-gray-800 px-1 rounded text-sm">$1</code>')
			.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
			.replace(/\*([^*]+)\*/g, '<em>$1</em>')
			.replace(/\n/g, '<br>');
	}
</script>

<svelte:head>
	<title>Debug Console | NAIA</title>
</svelte:head>

{#if !$isMasterMode}
	<!-- Master Login Required -->
	<div class="flex items-center justify-center min-h-[80vh]">
		<div class="w-full max-w-md p-8 bg-gray-900/50 backdrop-blur border border-gray-800 rounded-2xl shadow-2xl">
			<div class="text-center mb-8">
				<div class="text-4xl mb-4">🔐</div>
				<h1 class="text-2xl font-bold text-gray-100">Debug Console</h1>
				<p class="text-gray-400 mt-2">Master access required</p>
			</div>
			
			<form onsubmit={(e) => { e.preventDefault(); handleLogin(); }}>
				<div class="space-y-4">
					<div>
						<label class="block text-sm font-medium text-gray-400 mb-2">Master Token</label>
						<input
							type="password"
							bind:value={masterTokenInput}
							placeholder="Enter master token..."
							class="w-full px-4 py-3 bg-gray-800/50 border border-gray-700 rounded-lg text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-naia-500 focus:border-transparent"
						/>
					</div>
					
					{#if loginError}
						<div class="p-3 bg-red-900/30 border border-red-800 rounded-lg text-red-400 text-sm">
							{loginError}
						</div>
					{/if}
					
					<button
						type="submit"
						class="w-full py-3 bg-naia-600 hover:bg-naia-500 text-white font-medium rounded-lg transition-colors"
					>
						Authenticate
					</button>
				</div>
			</form>
			
			<div class="mt-6 pt-6 border-t border-gray-800">
				<p class="text-xs text-gray-500 text-center">
					Set <code class="text-gray-400">NAIA_MASTER_TOKEN</code> environment variable on the server
				</p>
			</div>
		</div>
	</div>
{:else}
	<!-- Debug Console -->
	<div class="flex flex-col h-[calc(100vh-8rem)]">
		<!-- Header -->
		<div class="flex items-center justify-between p-4 border-b border-gray-800">
			<div class="flex items-center gap-4">
				<div class="flex items-center gap-2">
					<span class="text-2xl">🤖</span>
					<div>
						<h1 class="text-xl font-bold text-gray-100">NAIA Debug Console</h1>
						<p class="text-xs text-gray-500">Claude-powered debugging assistant</p>
					</div>
				</div>
				<span class="px-2 py-1 bg-green-900/30 text-green-400 text-xs rounded-full border border-green-800">
					Master Mode Active
				</span>
			</div>
			
			<div class="flex items-center gap-2">
				<button
					onclick={() => showContext = !showContext}
					class="px-3 py-1.5 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 rounded-lg transition-colors"
				>
					{showContext ? 'Hide' : 'Show'} Context
				</button>
				<button
					onclick={clearChat}
					class="px-3 py-1.5 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 rounded-lg transition-colors"
				>
					Clear Chat
				</button>
			</div>
		</div>
		
		<!-- Context Panel (collapsible) -->
		{#if showContext && context}
			<div class="p-4 bg-gray-900/50 border-b border-gray-800">
				<div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 text-xs">
					{#each context.components as component}
						<div class="p-2 bg-gray-800/50 rounded-lg">
							<div class="font-medium text-gray-300">{component.name}</div>
							<div class="text-gray-500">Port {component.port}</div>
						</div>
					{/each}
				</div>
			</div>
		{/if}
		
		<!-- Chat Messages -->
		<div 
			bind:this={chatContainer}
			class="flex-1 overflow-y-auto p-4 space-y-4"
		>
			{#if messages.length === 0}
				<!-- Welcome / Suggestions -->
				<div class="flex flex-col items-center justify-center h-full text-center">
					<div class="text-6xl mb-4">🧠</div>
					<h2 class="text-xl font-semibold text-gray-300 mb-2">What can I help you debug?</h2>
					<p class="text-gray-500 mb-6 max-w-md">
						I have full access to NAIA's source code, databases, logs, and can even trigger builds. Ask me anything!
					</p>
					
					<div class="grid grid-cols-1 md:grid-cols-2 gap-2 max-w-2xl">
						{#each suggestedQueries as query}
							<button
								onclick={() => sendMessage(query)}
								class="p-3 text-left text-sm bg-gray-800/50 hover:bg-gray-800 border border-gray-700 hover:border-naia-600 rounded-lg text-gray-300 transition-all"
							>
								{query}
							</button>
						{/each}
					</div>
				</div>
			{:else}
				{#each messages as message, i (i)}
					<div class="flex gap-3 {message.role === 'user' ? 'justify-end' : ''}">
						{#if message.role === 'assistant'}
							<div class="flex-shrink-0 w-8 h-8 bg-naia-600 rounded-full flex items-center justify-center text-white text-sm">
								🤖
							</div>
						{/if}
						
						<div class="max-w-[80%] {message.role === 'user' 
							? 'bg-naia-600 text-white' 
							: 'bg-gray-800 text-gray-100'} rounded-2xl px-4 py-3">
							{#if message.role === 'assistant'}
								{@html formatMessage(message.content)}
							{:else}
								<div class="whitespace-pre-wrap">{message.content}</div>
							{/if}
						</div>
						
						{#if message.role === 'user'}
							<div class="flex-shrink-0 w-8 h-8 bg-gray-700 rounded-full flex items-center justify-center text-white text-sm">
								👤
							</div>
						{/if}
					</div>
				{/each}
				
				{#if isLoading}
					<div class="flex gap-3">
						<div class="flex-shrink-0 w-8 h-8 bg-naia-600 rounded-full flex items-center justify-center text-white text-sm animate-pulse">
							🤖
						</div>
						<div class="bg-gray-800 rounded-2xl px-4 py-3">
							<div class="flex items-center gap-2 text-gray-400">
								<span class="animate-spin">⚙️</span>
								<span>Thinking...</span>
							</div>
						</div>
					</div>
				{/if}
			{/if}
		</div>
		
		<!-- Input Area -->
		<div class="p-4 border-t border-gray-800 bg-gray-900/50">
			<div class="flex gap-3">
				<textarea
					bind:value={inputMessage}
					onkeydown={handleKeydown}
					placeholder="Ask me to debug something... (Enter to send, Shift+Enter for new line)"
					rows="2"
					disabled={isLoading}
					class="flex-1 px-4 py-3 bg-gray-800/50 border border-gray-700 rounded-xl text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-naia-500 focus:border-transparent resize-none disabled:opacity-50"
				></textarea>
				<button
					onclick={() => sendMessage()}
					disabled={isLoading || !inputMessage.trim()}
					class="px-6 py-3 bg-naia-600 hover:bg-naia-500 disabled:bg-gray-700 disabled:cursor-not-allowed text-white font-medium rounded-xl transition-colors"
				>
					{isLoading ? '...' : 'Send'}
				</button>
			</div>
			<div class="mt-2 text-xs text-gray-600 text-center">
				Claude has access to: source code, QuestDB, PostgreSQL, logs, build system
			</div>
		</div>
	</div>
{/if}
