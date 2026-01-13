<script lang="ts">
	import { onMount } from 'svelte';
	import { getPendingSuggestions, getSuggestion, approveSuggestion, rejectSuggestion, deferSuggestion, type Suggestion, type SuggestionDetail } from '$lib/services/api';
	import { pendingCount, toasts } from '$lib/stores/signalr';
	import Icon from '$lib/components/Icon.svelte';

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
			// Defensive: ensure data is always an array
			suggestions = Array.isArray(result?.data) ? result.data : [];
			totalSuggestions = result?.total ?? 0;
			pendingCount.set(totalSuggestions);
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load suggestions';
			suggestions = [];
			totalSuggestions = 0;
		} finally {
			loading = false;
		}
	}

	async function openDetail(suggestion: Suggestion) {
		loadingDetail = true;
		try {
			const detail = await getSuggestion(suggestion.id);
			// Defensive: ensure arrays are always defined and filter out nulls
			selectedSuggestion = {
				...detail,
				points: Array.isArray(detail?.points) ? detail.points.filter(p => p != null) : [],
				expectedRoles: Array.isArray(detail?.expectedRoles) ? detail.expectedRoles.filter(r => r != null) : []
			};
			console.log('Loaded suggestion detail:', {
				id: selectedSuggestion?.id,
				hasPoints: selectedSuggestion?.points ? 'yes' : 'no',
				pointsType: typeof selectedSuggestion?.points,
				pointsIsArray: Array.isArray(selectedSuggestion?.points),
				pointsLength: Array.isArray(selectedSuggestion?.points) ? selectedSuggestion.points.length : 'N/A',
				hasExpectedRoles: selectedSuggestion?.expectedRoles ? 'yes' : 'no',
				expectedRolesType: typeof selectedSuggestion?.expectedRoles,
				expectedRolesIsArray: Array.isArray(selectedSuggestion?.expectedRoles),
				expectedRolesLength: Array.isArray(selectedSuggestion?.expectedRoles) ? selectedSuggestion.expectedRoles.length : 'N/A'
			});
		} catch (e) {
			console.error('Error loading suggestion:', e);
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
			<Icon name="refresh" size="16" />
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
				<Icon name="patterns" size="20" class="text-naia-500" />
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
	{:else if !suggestions || !Array.isArray(suggestions) || suggestions.length === 0}
			<div class="card p-12 text-center">
				<div class="mx-auto w-16 h-16 bg-gray-100 dark:bg-gray-800 rounded-full flex items-center justify-center mb-4">
					<Icon name="check" size="32" class="text-gray-400" />
				</div>
				<h3 class="text-lg font-semibold text-gray-900 dark:text-gray-100">All caught up!</h3>
				<p class="text-gray-500 dark:text-gray-400 mt-1">
					No pending suggestions to review. New suggestions will appear as NAIA detects patterns in your data.
				</p>
			</div>
		{:else if suggestions && Array.isArray(suggestions) && suggestions.length > 0}
			{#each (suggestions || []) as suggestion}
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
							<Icon name="chevron-right" size="20" class="text-gray-400" />
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
					<Icon name="spinner" size="32" class="mx-auto text-naia-500" />
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
						<Icon name="close" size="20" />
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
						<h4 class="font-medium text-gray-900 dark:text-gray-100 mb-2">Matched Points ({selectedSuggestion?.points && Array.isArray(selectedSuggestion.points) ? selectedSuggestion.points.filter(p => p != null).length : 0})</h4>
						<div class="space-y-2">
							{#each (selectedSuggestion?.points && Array.isArray(selectedSuggestion.points) ? selectedSuggestion.points : []).filter(m => m != null) as match}
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
				{#if selectedSuggestion?.expectedRoles && Array.isArray(selectedSuggestion.expectedRoles) && selectedSuggestion.expectedRoles.length > 0}
					<div>
						<h4 class="font-medium text-gray-900 dark:text-gray-100 mb-2">Expected Pattern Roles</h4>
						<div class="space-y-2">
							{#each (selectedSuggestion?.expectedRoles || []).filter(r => r != null) as role}
									<div class="p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
										<div class="font-medium text-sm">{role?.name || 'Unknown'}</div>
										<div class="text-xs text-gray-500 mt-1">{role?.description || ''}</div>
										{#if role?.namingPatterns && Array.isArray(role.namingPatterns) && role.namingPatterns.length > 0}
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
							<Icon name="spinner" size="16" />
						{/if}
						Defer
					</button>
					<button 
						class="btn btn-danger"
						onclick={handleReject}
						disabled={!!processingAction}
					>
						{#if processingAction === 'reject'}
							<Icon name="spinner" size="16" />
						{/if}
						Reject
					</button>
					<button 
						class="btn btn-success"
						onclick={handleApprove}
						disabled={!!processingAction}
					>
						{#if processingAction === 'approve'}
							<Icon name="spinner" size="16" />
						{/if}
						Approve
					</button>
				</div>
			{/if}
		</div>
	</div>
{/if}
