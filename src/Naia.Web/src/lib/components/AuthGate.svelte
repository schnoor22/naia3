<script lang="ts">
	import { onMount } from 'svelte';
	
	let { children } = $props();
	
	let authenticated = $state(false);
	let password = $state('');
	let error = $state<string | null>(null);
	let shake = $state(false);
	let attempts = $state(0);
	
	const buzzwordRejections = [
		"Your synergy levels are insufficient.",
		"Unable to leverage your paradigm shift.",
		"That's not how we move the needle here.",
		"Your blockchain handshake failed.",
		"Insufficient AI-driven authentication tokens.",
		"Your thought leadership credentials expired.",
		"Can't disrupt without the right passphrase.",
		"Zero-trust says: trust me, that's wrong.",
		"Your digital transformation was denied.",
		"Machine learning predicts: nope.",
		"That's not very Web3 of you.",
		"Your agile sprint has been backlogged.",
		"Cloud-native authentication rejected.",
		"Your data-driven approach needs more data.",
		"Pivot detected. Pivot rejected.",
		"Your ecosystem integration failed validation.",
	];
	
	onMount(() => {
		// Check if already authenticated in this session
		if (typeof window !== 'undefined') {
			authenticated = sessionStorage.getItem('naia_auth') === 'true';
		}
	});
	
	function handleSubmit(e: Event) {
		e.preventDefault();
		
		if (password.toLowerCase() === 'flipper') {
			authenticated = true;
			if (typeof window !== 'undefined') {
				sessionStorage.setItem('naia_auth', 'true');
			}
		} else {
			attempts++;
			error = buzzwordRejections[Math.floor(Math.random() * buzzwordRejections.length)];
			shake = true;
			setTimeout(() => shake = false, 500);
			password = '';
		}
	}
	
	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter') {
			handleSubmit(e);
		}
	}
</script>

{#if authenticated}
	{@render children()}
{:else}
	<div class="fixed inset-0 z-[9999] flex items-center justify-center bg-gray-950/95 backdrop-blur-sm">
		<!-- Subtle animated background -->
		<div class="absolute inset-0 overflow-hidden opacity-5">
			<div class="absolute top-1/4 left-1/4 w-96 h-96 bg-naia-500 rounded-full blur-3xl animate-pulse"></div>
			<div class="absolute bottom-1/4 right-1/4 w-80 h-80 bg-emerald-500 rounded-full blur-3xl animate-pulse" style="animation-delay: 1s;"></div>
		</div>
		
		<!-- Auth card -->
		<div 
			class="relative z-10 w-full max-w-sm mx-4"
			class:animate-shake={shake}
		>
			<div class="bg-gray-900/50 backdrop-blur-xl border border-gray-800/50 rounded-2xl p-8 shadow-2xl">
				<!-- Logo -->
				<div class="flex flex-col items-center mb-8">
					<img src="/naia-full-logo.png" alt="NAIA" class="h-12 w-auto mb-3 opacity-90" />
					<p class="text-gray-500 text-xs tracking-widest uppercase">Industrial Historian</p>
				</div>
				
				<!-- Form -->
				<form onsubmit={handleSubmit}>
					<div class="space-y-4">
						<div class="relative">
							<input
								type="password"
								bind:value={password}
								onkeydown={handleKeydown}
								placeholder="Access code"
								class="w-full px-4 py-3 bg-gray-800/50 border border-gray-700/50 rounded-lg text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-naia-500/50 focus:border-naia-500/50 transition-all"
								autofocus
							/>
							<div class="absolute right-3 top-1/2 -translate-y-1/2 text-gray-600">
								<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
									<path stroke-linecap="round" stroke-linejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
								</svg>
							</div>
						</div>
						
						<button
							type="submit"
							class="w-full py-3 bg-naia-600 hover:bg-naia-500 text-white font-medium rounded-lg transition-colors"
						>
							Enter
						</button>
					</div>
				</form>
				
				<!-- Error message -->
				{#if error}
					<div class="mt-6 p-3 bg-red-500/10 border border-red-500/20 rounded-lg">
						<p class="text-red-400 text-sm text-center">{error}</p>
					</div>
				{/if}
				
				<!-- Attempt counter (subtle) -->
				{#if attempts > 2}
					<p class="mt-4 text-gray-600 text-xs text-center">
						{attempts} failed synergy attempts
					</p>
				{/if}
			</div>
			
			<!-- Barely visible hint after many attempts -->
			{#if attempts > 5}
				<p class="mt-6 text-gray-800 text-xs text-center tracking-wide">
					hint: 🐬
				</p>
			{/if}
		</div>
	</div>
{/if}

<style>
	@keyframes shake {
		0%, 100% { transform: translateX(0); }
		10%, 30%, 50%, 70%, 90% { transform: translateX(-4px); }
		20%, 40%, 60%, 80% { transform: translateX(4px); }
	}
	
	.animate-shake {
		animation: shake 0.5s ease-in-out;
	}
</style>
