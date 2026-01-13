<script lang="ts">
	import '../app.css';
	import { page } from '$app/stores';
	import { onMount } from 'svelte';
	import ThemeToggle from '$lib/components/ThemeToggle.svelte';
	import ConnectionStatus from '$lib/components/ConnectionStatus.svelte';
	import AuthGate from '$lib/components/AuthGate.svelte';
	import { pendingCount, connectionState } from '$lib/stores/signalr';
	import { initializeSignalR } from '$lib/services/signalr';

	let sidebarOpen = $state(true);
	
	// Build info for version display
	let buildInfo = $state<{ version: string; buildTimeDisplay: string; gitCommit?: string } | null>(null);

	const iconMap: Record<string, string> = {
		dashboard: '📊',
		points: '📍',
		trends: '📈',
		patterns: '🔍',
		ingestion: '📥',
		stack: '🔧',
		logs: '📄'
	};

	const navItems = [
		{ href: '/', label: 'Dashboard', icon: 'dashboard' },
		{ href: '/points', label: 'Point Browser', icon: 'points' },
		{ href: '/trends', label: 'Trend Viewer', icon: 'trends' },
		{ href: '/patterns', label: 'Pattern Review', icon: 'patterns' },
		{ href: '/ingestion', label: 'Ingestion', icon: 'ingestion' },
		{ href: '/stack', label: 'Technology Stack', icon: 'stack' },
		{ href: '/logs', label: 'System Logs', icon: 'logs' },
	];

	function isActive(href: string, currentPath: string): boolean {
		if (href === '/') return currentPath === '/';
		return currentPath.startsWith(href);
	}

	onMount(async () => {
		initializeSignalR();
		
		// Load build info
		try {
			const response = await fetch('/BUILD_INFO.json');
			if (response.ok) {
				buildInfo = await response.json();
			}
		} catch (e) {
			console.log('Build info not available');
		}
	});
</script>

<svelte:head>
	<title>NAIA Command Center</title>
</svelte:head>

<AuthGate>
<div class="flex h-screen overflow-hidden">
	<!-- Sidebar -->
	<aside 
		class="fixed inset-y-0 left-0 z-50 flex flex-col bg-white dark:bg-gray-900 border-r border-gray-200 dark:border-gray-800 transition-all duration-300"
		class:w-64={sidebarOpen}
		class:w-20={!sidebarOpen}
	>
		<!-- Logo -->
		<div class="flex items-center h-16 px-4 border-b border-gray-200 dark:border-gray-800">
			<a href="/" class="flex items-center gap-3">
				<img src="/naia-full-logo.png" alt="NAIA" class="h-10 w-auto" />
				{#if sidebarOpen}
					<div class="flex flex-col">
						<span class="font-bold text-lg text-naia-600 dark:text-naia-400">NAIA</span>
						<span class="text-[10px] text-gray-500 dark:text-gray-400 -mt-1">Command Center</span>
					</div>
				{/if}
			</a>
		</div>

		<!-- Navigation -->
		<nav class="flex-1 p-4 space-y-1 overflow-y-auto">
			{#each navItems as item}
				{@const active = isActive(item.href, $page.url.pathname)}
				<a
					href={item.href}
					class="nav-link"
					class:active
					aria-label={item.label}
				>
					<span class="flex-shrink-0 w-6 h-6 flex items-center justify-center text-lg">
						{iconMap[item.icon] || '•'}
					</span>
					{#if sidebarOpen}
						<span class="truncate">{item.label}</span>
						{#if item.icon === 'patterns' && $pendingCount > 0}
							<span class="ml-auto bg-naia-500 text-white text-xs font-bold px-2 py-0.5 rounded-full">
								{$pendingCount}
							</span>
						{/if}
					{/if}
				</a>
			{/each}
		</nav>

		<!-- Sidebar footer -->
		<div class="p-4 border-t border-gray-200 dark:border-gray-800">
			{#if sidebarOpen}
				<div class="flex items-center justify-between mb-3">
					<ConnectionStatus />
					<ThemeToggle />
				</div>
				<a 
					href="/hangfire" 
					target="_blank"
					class="flex items-center gap-2 text-xs text-gray-500 dark:text-gray-400 hover:text-naia-500"
				>
					⚙️
					Hangfire Jobs
				</a>
				<!-- Version display -->
				{#if buildInfo}
					<div class="mt-2 pt-2 border-t border-gray-200 dark:border-gray-700">
						<div class="text-[10px] text-gray-400 dark:text-gray-500 font-mono">
							v{buildInfo.version}{#if buildInfo.gitCommit} ({buildInfo.gitCommit}){/if}
						</div>
						<div class="text-[9px] text-gray-400 dark:text-gray-600">
							Built: {buildInfo.buildTimeDisplay}
						</div>
					</div>
				{/if}
			{:else}
				<ThemeToggle />
			{/if}
		</div>

		<!-- Toggle button -->
		<button 
			onclick={() => sidebarOpen = !sidebarOpen}
			class="absolute top-20 -right-3 w-6 h-6 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-full flex items-center justify-center shadow-sm hover:shadow-md transition-shadow"
		>
			<span class="transition-transform" class:rotate-180={!sidebarOpen}>
				<Icon name="chevron-left" size="16" class="text-gray-500" />
			</span>
		</button>
	</aside>

	<!-- Main content -->
	<main 
		class="flex-1 overflow-auto transition-all duration-300"
		class:ml-64={sidebarOpen}
		class:ml-20={!sidebarOpen}
	>
		<div class="p-6">
			<slot />
		</div>
	</main>
</div>
</AuthGate>