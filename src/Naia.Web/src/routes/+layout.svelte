<script lang="ts">
	import '../app.css';
	import { page } from '$app/stores';
	import { onMount } from 'svelte';
	import ThemeToggle from '$lib/components/ThemeToggle.svelte';
	import ConnectionStatus from '$lib/components/ConnectionStatus.svelte';
	import { pendingCount, connectionState } from '$lib/stores/signalr';
	import { initializeSignalR } from '$lib/services/signalr';

	let sidebarOpen = $state(true);

	const navItems = [
		{ href: '/', label: 'Dashboard', icon: 'dashboard' },
		{ href: '/points', label: 'Point Browser', icon: 'points' },
		{ href: '/trends', label: 'Trend Viewer', icon: 'trends' },
		{ href: '/patterns', label: 'Pattern Review', icon: 'patterns' },
		{ href: '/ingestion', label: 'Ingestion', icon: 'ingestion' },
	];

	function isActive(href: string, currentPath: string): boolean {
		if (href === '/') return currentPath === '/';
		return currentPath.startsWith(href);
	}

	onMount(() => {
		initializeSignalR();
	});
</script>

<svelte:head>
	<title>NAIA Command Center</title>
</svelte:head>

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
					title={!sidebarOpen ? item.label : undefined}
				>
					<span class="flex-shrink-0 w-6 h-6 flex items-center justify-center">
						{#if item.icon === 'dashboard'}
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
								<path stroke-linecap="round" stroke-linejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" />
							</svg>
						{:else if item.icon === 'points'}
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
								<path stroke-linecap="round" stroke-linejoin="round" d="M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 010 3.75H5.625a1.875 1.875 0 010-3.75z" />
							</svg>
						{:else if item.icon === 'trends'}
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
								<path stroke-linecap="round" stroke-linejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z" />
							</svg>
						{:else if item.icon === 'patterns'}
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
								<path stroke-linecap="round" stroke-linejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456zM16.894 20.567L16.5 21.75l-.394-1.183a2.25 2.25 0 00-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 001.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 001.423 1.423l1.183.394-1.183.394a2.25 2.25 0 00-1.423 1.423z" />
							</svg>
						{:else if item.icon === 'ingestion'}
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
								<path stroke-linecap="round" stroke-linejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
							</svg>
						{/if}
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
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
						<path stroke-linecap="round" stroke-linejoin="round" d="M10.343 3.94c.09-.542.56-.94 1.11-.94h1.093c.55 0 1.02.398 1.11.94l.149.894c.07.424.384.764.78.93.398.164.855.142 1.205-.108l.737-.527a1.125 1.125 0 011.45.12l.773.774c.39.389.44 1.002.12 1.45l-.527.737c-.25.35-.272.806-.107 1.204.165.397.505.71.93.78l.893.15c.543.09.94.56.94 1.109v1.094c0 .55-.397 1.02-.94 1.11l-.893.149c-.425.07-.765.383-.93.78-.165.398-.143.854.107 1.204l.527.738c.32.447.269 1.06-.12 1.45l-.774.773a1.125 1.125 0 01-1.449.12l-.738-.527c-.35-.25-.806-.272-1.203-.107-.397.165-.71.505-.781.929l-.149.894c-.09.542-.56.94-1.11.94h-1.094c-.55 0-1.019-.398-1.11-.94l-.148-.894c-.071-.424-.384-.764-.781-.93-.398-.164-.854-.142-1.204.108l-.738.527c-.447.32-1.06.269-1.45-.12l-.773-.774a1.125 1.125 0 01-.12-1.45l.527-.737c.25-.35.273-.806.108-1.204-.165-.397-.505-.71-.93-.78l-.894-.15c-.542-.09-.94-.56-.94-1.109v-1.094c0-.55.398-1.02.94-1.11l.894-.149c.424-.07.765-.383.93-.78.165-.398.143-.854-.107-1.204l-.527-.738a1.125 1.125 0 01.12-1.45l.773-.773a1.125 1.125 0 011.45-.12l.737.527c.35.25.807.272 1.204.107.397-.165.71-.505.78-.929l.15-.894z" />
						<path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
					</svg>
					Hangfire Jobs
				</a>
			{:else}
				<ThemeToggle />
			{/if}
		</div>

		<!-- Toggle button -->
		<button 
			onclick={() => sidebarOpen = !sidebarOpen}
			class="absolute top-20 -right-3 w-6 h-6 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-full flex items-center justify-center shadow-sm hover:shadow-md transition-shadow"
		>
			<svg 
				xmlns="http://www.w3.org/2000/svg" 
				fill="none" 
				viewBox="0 0 24 24" 
				stroke-width="2" 
				stroke="currentColor" 
				class="w-4 h-4 text-gray-500 transition-transform"
				class:rotate-180={!sidebarOpen}
			>
				<path stroke-linecap="round" stroke-linejoin="round" d="M15.75 19.5L8.25 12l7.5-7.5" />
			</svg>
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
