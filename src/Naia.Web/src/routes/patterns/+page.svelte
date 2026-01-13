<script lang="ts">
	import { onMount } from 'svelte';
	import { getPendingSuggestions, getSuggestion, approveSuggestion, rejectSuggestion, deferSuggestion, type Suggestion, type SuggestionDetail } from '$lib/services/api';
	import { pendingCount, toasts } from '$lib/stores/signalr';

	let suggestions = $state<Suggestion[]>([]);
	let totalSuggestions = $state(0);
	let loading = $state(true);
	let error = $state<string | null>(null);

	onMount(() => {
		loadSuggestions();
	});

	// Detail view
	let selectedSuggestion = $state<SuggestionDetail | null>(null);
	let loadingDetail = $state(false);
	let rejectionReason = $state('');
	let processingAction = $state<string | null>(null);

	async function loadSuggestions() {
		loading = true;
		error = null;
		try {
			const result = await getPendingSuggestions(0, 50);
			suggestions = result.data;
			totalSuggestions = result.total;
			pendingCount.set(result.total);
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load suggestions';
		} finally {
			loading = false;
		}
	}

	async function openDetail(suggestion: Suggestion) {
		loadingDetail = true;
		try {
			selectedSuggestion = await getSuggestion(suggestion.id);
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Failed to load details',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			loadingDetail = false;
		}
	}

	async function handleApprove() {
		if (!selectedSuggestion) return;
		processingAction = 'approve';
		try {
			await approveSuggestion(selectedSuggestion.id);
			toasts.add({
				type: 'success',
				title: 'Pattern Approved!',
				message: 'NAIA is learning from your feedback'
			});
			selectedSuggestion = null;
			loadSuggestions();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Approval failed',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			processingAction = null;
		}
	}

	async function handleReject() {
		if (!selectedSuggestion) return;
		processingAction = 'reject';
		try {
			await rejectSuggestion(selectedSuggestion.id, rejectionReason || undefined);
			toasts.add({
				type: 'info',
				title: 'Feedback recorded',
				message: 'NAIA will improve based on your rejection'
			});
			selectedSuggestion = null;
			rejectionReason = '';
			loadSuggestions();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Rejection failed',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			processingAction = null;
		}
	}

	async function handleDefer() {
		if (!selectedSuggestion) return;
		processingAction = 'defer';
		try {
			await deferSuggestion(selectedSuggestion.id);
			toasts.add({
				type: 'info',
				title: 'Suggestion deferred',
				message: 'You can review this later'
			});
			selectedSuggestion = null;
			loadSuggestions();
		} catch (e) {
			toasts.add({
				type: 'error',
				title: 'Defer failed',
				message: e instanceof Error ? e.message : 'Unknown error'
			});
		} finally {
			processingAction = null;
		}
	}

	function formatConfidence(confidence: number): string {
		return `${Math.round(confidence * 100)}%`;
	}

	function formatDate(date: string): string {
		return new Date(date).toLocaleString();
	}

	function getConfidenceColor(confidence: number): string {
		if (confidence >= 0.8) return 'text-emerald-500';
		if (confidence >= 0.6) return 'text-amber-500';
		return 'text-red-500';
	}
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div>
			<h1 class="text-2xl font-bold text-gray-900 dark:text-gray-100">Pattern Review</h1>
			<p class="text-gray-500 dark:text-gray-400">Review and approve AI-detected patterns to help NAIA learn</p>
		</div>
		<button class="btn btn-secondary" onclick={loadSuggestions} disabled={loading}>
			<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
				<path stroke-linecap="round" stroke-linejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
			</svg>
			Refresh
		</button>
	</div>

	{#if error}
		<div class="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-red-700 dark:text-red-400">
			{error}
		</div>
	{/if}

	<!-- Info Banner -->
	<div class="card p-4 bg-naia-500/5 border-naia-500/20">
		<div class="flex items-start gap-3">
			<div class="p-2 bg-naia-500/10 rounded-lg">
				<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5 text-naia-500">
					<path stroke-linecap="round" stroke-linejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z" />
				</svg>
			</div>
			<div>
				<h3 class="font-medium text-gray-900 dark:text-gray-100">How Pattern Learning Works</h3>
				<p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
					NAIA analyzes your data to detect equipment patterns. When you <strong>approve</strong> a suggestion, 
					the pattern confidence increases and NAIA learns to recognize similar configurations. 
					When you <strong>reject</strong>, NAIA learns to avoid similar false positives.
				</p>
			</div>
		</div>
	</div>

	<!-- Suggestions List -->
	<div class="grid grid-cols-1 gap-4">
		{#if loading}
			{#each Array(3) as _}
				<div class="card p-4">
					<div class="animate-pulse space-y-3">
						<div class="h-5 w-48 bg-gray-200 dark:bg-gray-700 rounded"></div>
						<div class="h-4 w-64 bg-gray-200 dark:bg-gray-700 rounded"></div>
						<div class="flex gap-2">
							<div class="h-6 w-16 bg-gray-200 dark:bg-gray-700 rounded"></div>
							<div class="h-6 w-24 bg-gray-200 dark:bg-gray-700 rounded"></div>
						</div>
					</div>
				</div>
			{/each}
		{:else if !suggestions || suggestions.length === 0}
			<div class="card p-12 text-center">
				<div class="mx-auto w-16 h-16 bg-gray-100 dark:bg-gray-800 rounded-full flex items-center justify-center mb-4">
					<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-8 h-8 text-gray-400">
						<path stroke-linecap="round" stroke-linejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
					</svg>
				</div>
				<h3 class="text-lg font-semibold text-gray-900 dark:text-gray-100">All caught up!</h3>
				<p class="text-gray-500 dark:text-gray-400 mt-1">
					No pending suggestions to review. New suggestions will appear as NAIA detects patterns in your data.
				</p>
			</div>
		{:else if suggestions && suggestions.length > 0}
			{#each suggestions as suggestion}
				<button 
					class="card p-4 text-left hover:shadow-lg transition-shadow w-full"
					onclick={() => openDetail(suggestion)}
				>
					<div class="flex items-start justify-between">
						<div>
							<h3 class="font-semibold text-gray-900 dark:text-gray-100">{suggestion.patternName}</h3>
							<p class="text-sm text-gray-500 dark:text-gray-400 mt-1">
								Detected {formatDate(suggestion.createdAt)}
							</p>
						</div>
						<div class="flex items-center gap-3">
							<div class="text-right">
								<div class="text-sm text-gray-500">Confidence</div>
								<div class="text-lg font-bold {getConfidenceColor(suggestion.confidence)}">
									{formatConfidence(suggestion.confidence)}
								</div>
							</div>
							<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5 text-gray-400">
								<path stroke-linecap="round" stroke-linejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
							</svg>
						</div>
					</div>
				</button>
			{/each}
		{/if}
	</div>
</div>

<!-- Detail Modal -->
{#if selectedSuggestion || loadingDetail}
	<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onclick={() => !processingAction && (selectedSuggestion = null)}>
		<div class="bg-white dark:bg-gray-900 rounded-xl shadow-2xl w-full max-w-2xl m-4 max-h-[90vh] overflow-hidden" onclick={(e) => e.stopPropagation()}>
			{#if loadingDetail}
				<div class="p-8 text-center">
					<svg class="w-8 h-8 animate-spin mx-auto text-naia-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
						<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
						<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
					</svg>
					<p class="mt-2 text-gray-500">Loading details...</p>
				</div>
			{:else if selectedSuggestion && selectedSuggestion.id}
				<!-- Header -->
				<div class="flex items-center justify-between p-4 border-b border-gray-200 dark:border-gray-800">
					<div>
						<h3 class="text-lg font-semibold text-gray-900 dark:text-gray-100">{selectedSuggestion.patternName}</h3>
						<p class="text-sm text-gray-500">{selectedSuggestion.reason}</p>
					</div>
					<button 
						class="p-2 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg"
						onclick={() => selectedSuggestion = null}
						disabled={!!processingAction}
					>
						<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5">
							<path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
						</svg>
					</button>
				</div>

				<!-- Content -->
				<div class="p-4 space-y-4 max-h-[60vh] overflow-y-auto">
					<!-- Confidence -->
					<div class="flex items-center gap-4">
						<div class="flex-1">
							<div class="text-sm text-gray-500 mb-1">Overall Confidence</div>
							<div class="h-3 bg-gray-200 dark:bg-gray-700 rounded-full overflow-hidden">
								<div 
									class="h-full bg-naia-500 transition-all"
									style="width: {selectedSuggestion.confidence * 100}%"
								></div>
							</div>
						</div>
						<div class="text-2xl font-bold {getConfidenceColor(selectedSuggestion.confidence)}">
							{formatConfidence(selectedSuggestion.confidence)}
						</div>
					</div>

					<!-- Score Breakdown -->
					<div class="grid grid-cols-2 gap-3 p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
						<div>
							<div class="text-xs text-gray-500">Naming</div>
							<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatConfidence(selectedSuggestion.namingScore)}</div>
						</div>
						<div>
							<div class="text-xs text-gray-500">Correlation</div>
							<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatConfidence(selectedSuggestion.correlationScore)}</div>
						</div>
						<div>
							<div class="text-xs text-gray-500">Range</div>
							<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatConfidence(selectedSuggestion.rangeScore)}</div>
						</div>
						<div>
							<div class="text-xs text-gray-500">Rate</div>
							<div class="text-lg font-semibold text-gray-900 dark:text-gray-100">{formatConfidence(selectedSuggestion.rateScore)}</div>
						</div>
					</div>

					<!-- Matched Points -->
					<div>
						<h4 class="font-medium text-gray-900 dark:text-gray-100 mb-2">Matched Points ({selectedSuggestion.points?.length || 0})</h4>
						<div class="space-y-2">
							{#each selectedSuggestion.points || [] as match}
								<div class="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
									<div>
										<div class="font-mono text-sm">{match.pointName}</div>
										{#if match.suggestedRole}
											<div class="text-xs text-gray-500">Suggested: {match.suggestedRole}</div>
										{/if}
									</div>
									{#if match.roleConfidence}
										<span class="badge badge-info">
											{Math.round(match.roleConfidence * 100)}%
										</span>
									{/if}
								</div>
							{/each}
						</div>
					</div>

					<!-- Expected Roles -->
					{#if selectedSuggestion.expectedRoles && selectedSuggestion.expectedRoles.length > 0}
						<div>
							<h4 class="font-medium text-gray-900 dark:text-gray-100 mb-2">Expected Pattern Roles</h4>
							<div class="space-y-2">
								{#each selectedSuggestion.expectedRoles as role}
									<div class="p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
										<div class="font-medium text-sm">{role?.name || 'Unknown'}</div>
										<div class="text-xs text-gray-500 mt-1">{role?.description || ''}</div>
										{#if role?.namingPatterns && role.namingPatterns.length > 0}
											<div class="text-xs text-gray-400 mt-1">Patterns: {role.namingPatterns.join(', ')}</div>
										{/if}
									</div>
								{/each}
							</div>
						</div>
					{/if}

					<!-- Rejection Reason (shown when rejecting) -->
					<div>
						<label for="rejection-reason" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
							Rejection Reason (optional)
						</label>
						<textarea
							id="rejection-reason"
							class="input resize-none"
							rows="2"
							placeholder="Help NAIA learn why this pattern doesn't match..."
							bind:value={rejectionReason}
						></textarea>
					</div>
				</div>

				<!-- Actions -->
				<div class="flex items-center justify-end gap-3 p-4 border-t border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-800/50">
					<button 
						class="btn btn-ghost"
						onclick={handleDefer}
						disabled={!!processingAction}
					>
						{#if processingAction === 'defer'}
							<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
								<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
								<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
							</svg>
						{/if}
						Defer
					</button>
					<button 
						class="btn btn-danger"
						onclick={handleReject}
						disabled={!!processingAction}
					>
						{#if processingAction === 'reject'}
							<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
								<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
								<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
							</svg>
						{/if}
						Reject
					</button>
					<button 
						class="btn btn-success"
						onclick={handleApprove}
						disabled={!!processingAction}
					>
						{#if processingAction === 'approve'}
							<svg class="w-4 h-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
								<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
								<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
							</svg>
						{/if}
						Approve
					</button>
				</div>
			{/if}
		</div>
	</div>
{/if}
