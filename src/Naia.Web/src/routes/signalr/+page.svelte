<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, fly, scale } from 'svelte/transition';
	import * as signalR from '@microsoft/signalr';
	
	interface HubMessage {
		id: string;
		direction: 'in' | 'out';
		method: string;
		args: any[];
		timestamp: Date;
	}
	
	let connection: signalR.HubConnection | null = null;
	let connectionState = $state<'disconnected' | 'connecting' | 'connected' | 'error'>('disconnected');
	let messages = $state<HubMessage[]>([]);
	let hubUrl = $state('/hubs/data');
	let methodName = $state('SubscribeToPoint');
	let methodArgs = $state('[1]');
	let error = $state<string | null>(null);
	let subscribedEvents = $state<string[]>([]);
	let messagesContainer: HTMLDivElement;
	
	// Common SignalR methods for NAIA
	const commonMethods = [
		{ name: 'SubscribeToPoint', args: '[1]', description: 'Subscribe to real-time updates for a point' },
		{ name: 'UnsubscribeFromPoint', args: '[1]', description: 'Stop receiving updates for a point' },
		{ name: 'SubscribeToEquipment', args: '[1]', description: 'Subscribe to all points for equipment' },
		{ name: 'UnsubscribeFromEquipment', args: '[1]', description: 'Stop updates for equipment' },
		{ name: 'GetCurrentValue', args: '[1]', description: 'Get the current value of a point' },
		{ name: 'Ping', args: '[]', description: 'Test connection latency' },
	];
	
	// Events we expect to receive
	const knownEvents = [
		'ReceivePointUpdate',
		'ReceiveAlarm',
		'ReceiveBatchUpdate',
		'ConnectionStatus',
		'Error'
	];
	
	onDestroy(() => {
		disconnect();
	});
	
	async function connect() {
		if (connection) {
			await disconnect();
		}
		
		connectionState = 'connecting';
		error = null;
		
		try {
			connection = new signalR.HubConnectionBuilder()
				.withUrl(hubUrl)
				.withAutomaticReconnect()
				.configureLogging(signalR.LogLevel.Information)
				.build();
			
			// Set up event handlers for known events
			for (const event of knownEvents) {
				connection.on(event, (...args) => {
					addMessage('in', event, args);
				});
				if (!subscribedEvents.includes(event)) {
					subscribedEvents = [...subscribedEvents, event];
				}
			}
			
			connection.onclose((err) => {
				connectionState = 'disconnected';
				if (err) {
					error = err.message;
					addMessage('in', 'Connection Closed', [err.message]);
				}
			});
			
			connection.onreconnecting((err) => {
				connectionState = 'connecting';
				addMessage('in', 'Reconnecting', [err?.message]);
			});
			
			connection.onreconnected((connectionId) => {
				connectionState = 'connected';
				addMessage('in', 'Reconnected', [connectionId]);
			});
			
			await connection.start();
			connectionState = 'connected';
			addMessage('in', 'Connected', [connection.connectionId]);
			
		} catch (e: any) {
			connectionState = 'error';
			error = e.message || 'Failed to connect';
			addMessage('in', 'Connection Error', [e.message]);
		}
	}
	
	async function disconnect() {
		if (connection) {
			await connection.stop();
			connection = null;
		}
		connectionState = 'disconnected';
		addMessage('in', 'Disconnected', []);
	}
	
	async function sendMessage() {
		if (!connection || connectionState !== 'connected') {
			error = 'Not connected';
			return;
		}
		
		let parsedArgs: any[];
		try {
			parsedArgs = JSON.parse(methodArgs);
			if (!Array.isArray(parsedArgs)) {
				parsedArgs = [parsedArgs];
			}
		} catch {
			error = 'Invalid JSON arguments';
			return;
		}
		
		error = null;
		addMessage('out', methodName, parsedArgs);
		
		try {
			const result = await connection.invoke(methodName, ...parsedArgs);
			if (result !== undefined) {
				addMessage('in', `${methodName} Response`, [result]);
			}
		} catch (e: any) {
			error = e.message;
			addMessage('in', `${methodName} Error`, [e.message]);
		}
	}
	
	function addMessage(direction: 'in' | 'out', method: string, args: any[]) {
		messages = [...messages, {
			id: crypto.randomUUID(),
			direction,
			method,
			args,
			timestamp: new Date()
		}];
		
		// Scroll to bottom
		requestAnimationFrame(() => {
			messagesContainer?.scrollTo({ top: messagesContainer.scrollHeight, behavior: 'smooth' });
		});
	}
	
	function loadPreset(preset: typeof commonMethods[0]) {
		methodName = preset.name;
		methodArgs = preset.args;
	}
	
	function clearMessages() {
		messages = [];
	}
	
	function getStateColor(state: typeof connectionState): string {
		switch (state) {
			case 'connected': return 'bg-emerald-500';
			case 'connecting': return 'bg-amber-500 animate-pulse';
			case 'error': return 'bg-red-500';
			default: return 'bg-slate-500';
		}
	}
	
	function formatArgs(args: any[]): string {
		try {
			return JSON.stringify(args, null, 2);
		} catch {
			return String(args);
		}
	}
</script>

<svelte:head>
	<title>SignalR Tester | NAIA</title>
</svelte:head>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-semibold text-white flex items-center gap-3">
				<span class="text-3xl">📡</span>
				SignalR Hub Tester
			</h1>
			<p class="text-slate-400 mt-1">Send and receive real-time hub messages</p>
		</div>
		<div class="flex items-center gap-2">
			<div class="w-3 h-3 rounded-full {getStateColor(connectionState)}"></div>
			<span class="text-sm text-slate-400 capitalize">{connectionState}</span>
		</div>
	</div>
	
	{#if error}
		<div class="bg-red-500/10 border border-red-500/30 rounded-lg p-4 text-red-400" in:fade>
			{error}
		</div>
	{/if}
	
	<div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
		<!-- Connection & Methods -->
		<div class="space-y-4">
			<!-- Connection -->
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
					<h2 class="font-medium text-white">Connection</h2>
				</div>
				<div class="p-4 space-y-4">
					<div>
						<label class="block text-sm text-slate-400 mb-2">Hub URL</label>
						<input
							type="text"
							bind:value={hubUrl}
							disabled={connectionState === 'connected'}
							class="w-full px-4 py-2 bg-slate-800 border border-slate-700 rounded-lg text-white text-sm focus:outline-none focus:ring-2 focus:ring-teal-500 disabled:opacity-50"
						/>
					</div>
					<div class="flex gap-2">
						{#if connectionState === 'connected'}
							<button
								onclick={disconnect}
								class="flex-1 px-4 py-2 bg-red-600 hover:bg-red-500 text-white rounded-lg text-sm transition-colors"
							>
								Disconnect
							</button>
						{:else}
							<button
								onclick={connect}
								disabled={connectionState === 'connecting'}
								class="flex-1 px-4 py-2 bg-teal-600 hover:bg-teal-500 disabled:bg-slate-700 text-white rounded-lg text-sm transition-colors"
							>
								{connectionState === 'connecting' ? 'Connecting...' : 'Connect'}
							</button>
						{/if}
					</div>
				</div>
			</div>
			
			<!-- Common Methods -->
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
					<h2 class="font-medium text-white">Quick Methods</h2>
				</div>
				<div class="max-h-80 overflow-y-auto">
					{#each commonMethods as method}
						<button
							onclick={() => loadPreset(method)}
							class="w-full px-4 py-3 text-left hover:bg-slate-800/50 border-b border-slate-700/30 transition-colors"
						>
							<div class="font-mono text-sm text-teal-400">{method.name}</div>
							<p class="text-xs text-slate-500 mt-1">{method.description}</p>
						</button>
					{/each}
				</div>
			</div>
			
			<!-- Subscribed Events -->
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
					<h2 class="font-medium text-white">Listening For</h2>
				</div>
				<div class="p-4">
					<div class="flex flex-wrap gap-2">
						{#each subscribedEvents as event}
							<span class="px-2 py-1 bg-slate-800 border border-slate-700 rounded text-xs text-slate-300">
								{event}
							</span>
						{/each}
					</div>
				</div>
			</div>
		</div>
		
		<!-- Message Sender -->
		<div class="space-y-4">
			<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
				<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50">
					<h2 class="font-medium text-white">Send Message</h2>
				</div>
				<div class="p-4 space-y-4">
					<div>
						<label class="block text-sm text-slate-400 mb-2">Method Name</label>
						<input
							type="text"
							bind:value={methodName}
							placeholder="MethodName"
							class="w-full px-4 py-2 bg-slate-800 border border-slate-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:ring-2 focus:ring-teal-500"
						/>
					</div>
					<div>
						<label class="block text-sm text-slate-400 mb-2">Arguments (JSON Array)</label>
						<textarea
							bind:value={methodArgs}
							placeholder='[arg1, arg2, ...]'
							rows="3"
							class="w-full px-4 py-2 bg-slate-800 border border-slate-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:ring-2 focus:ring-teal-500 resize-y"
						></textarea>
					</div>
					<button
						onclick={sendMessage}
						disabled={connectionState !== 'connected'}
						class="w-full px-4 py-2 bg-teal-600 hover:bg-teal-500 disabled:bg-slate-700 text-white rounded-lg text-sm transition-colors"
					>
						Send →
					</button>
				</div>
			</div>
		</div>
		
		<!-- Message Log -->
		<div class="bg-slate-900 rounded-xl border border-slate-700/50 overflow-hidden">
			<div class="px-4 py-3 border-b border-slate-700/50 bg-slate-800/50 flex items-center justify-between">
				<h2 class="font-medium text-white">Message Log</h2>
				<button
					onclick={clearMessages}
					class="text-xs text-slate-500 hover:text-white transition-colors"
				>
					Clear
				</button>
			</div>
			<div 
				bind:this={messagesContainer}
				class="h-[500px] overflow-y-auto"
			>
				{#if messages.length === 0}
					<div class="p-4 text-center text-slate-500">
						No messages yet. Connect and send a message.
					</div>
				{:else}
					{#each messages as msg, i (msg.id)}
						<div 
							class="px-4 py-3 border-b border-slate-700/30 {msg.direction === 'out' ? 'bg-teal-500/5' : ''}"
							in:fly={{ x: msg.direction === 'out' ? 20 : -20, duration: 200 }}
						>
							<div class="flex items-center gap-2 mb-1">
								<span class="text-lg">{msg.direction === 'out' ? '→' : '←'}</span>
								<span class="font-mono text-sm {msg.direction === 'out' ? 'text-teal-400' : 'text-purple-400'}">
									{msg.method}
								</span>
								<span class="text-xs text-slate-600 ml-auto">
									{msg.timestamp.toLocaleTimeString()}
								</span>
							</div>
							{#if msg.args.length > 0}
								<pre class="text-xs text-slate-400 bg-slate-800/50 rounded p-2 mt-2 overflow-x-auto">{formatArgs(msg.args)}</pre>
							{/if}
						</div>
					{/each}
				{/if}
			</div>
		</div>
	</div>
</div>
