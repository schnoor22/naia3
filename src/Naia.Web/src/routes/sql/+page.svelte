<script lang="ts">
	import { onMount } from 'svelte';
	import { isMasterMode, getMasterHeaders } from '$lib/stores/master';
	
	let query = $state(`-- Quick queries:
-- SELECT * FROM points LIMIT 20;
-- SELECT * FROM data_sources;
-- SELECT * FROM patterns WHERE is_active = true;
-- SELECT * FROM suggestions WHERE status = 'Pending' LIMIT 10;
-- SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';

SELECT COUNT(*) as count, table_name 
FROM information_schema.tables 
WHERE table_schema = 'public' 
GROUP BY table_name;`);
	
	let results = $state<any[]>([]);
	let columns = $state<string[]>([]);
	let error = $state<string | null>(null);
	let isLoading = $state(false);
	let executionTime = $state<number | null>(null);
	let rowCount = $state(0);
	
	// Saved queries
	const savedQueries = [
		{ name: 'All Tables', query: `SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;` },
		{ name: 'Points Summary', query: `SELECT COUNT(*) as total, data_source_id FROM points GROUP BY data_source_id;` },
		{ name: 'Data Sources', query: `SELECT id, name, type, status, created_at FROM data_sources ORDER BY created_at DESC;` },
		{ name: 'Recent Points', query: `SELECT id, tag_name, description, eng_units, created_at FROM points ORDER BY created_at DESC LIMIT 50;` },
		{ name: 'Active Patterns', query: `SELECT id, name, description, confidence, match_count FROM patterns WHERE is_active = true ORDER BY confidence DESC;` },
		{ name: 'Pending Suggestions', query: `SELECT id, pattern_id, confidence, status, created_at FROM suggestions WHERE status = 'Pending' ORDER BY confidence DESC LIMIT 20;` },
		{ name: 'Recent Logs', query: `SELECT timestamp, level, message FROM logs ORDER BY timestamp DESC LIMIT 100;` },
		{ name: 'Table Sizes', query: `SELECT relname as table, pg_size_pretty(pg_total_relation_size(relid)) as size FROM pg_catalog.pg_statio_user_tables ORDER BY pg_total_relation_size(relid) DESC;` },
	];
	
	async function executeQuery() {
		if (!query.trim()) return;
		
		// Basic safety check
		const normalizedQuery = query.trim().toUpperCase();
		if (!normalizedQuery.startsWith('SELECT') && !$isMasterMode) {
			error = 'Only SELECT queries allowed. Enable Master Mode for write operations.';
			return;
		}
		
		isLoading = true;
		error = null;
		results = [];
		columns = [];
		
		const startTime = performance.now();
		
		try {
			const response = await fetch('/api/admin/sql', {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					...getMasterHeaders()
				},
				body: JSON.stringify({ query: query.trim() })
			});
			
			const data = await response.json();
			
			if (!response.ok) {
				throw new Error(data.error || `HTTP ${response.status}`);
			}
			
			executionTime = performance.now() - startTime;
			
			if (data.columns && data.rows) {
				columns = data.columns;
				results = data.rows;
				rowCount = data.rowCount ?? data.rows.length;
			} else if (data.affectedRows !== undefined) {
				// For INSERT/UPDATE/DELETE
				results = [{ result: `${data.affectedRows} rows affected` }];
				columns = ['result'];
				rowCount = data.affectedRows;
			}
		} catch (e: any) {
			error = e.message;
			executionTime = performance.now() - startTime;
		} finally {
			isLoading = false;
		}
	}
	
	function loadQuery(q: string) {
		query = q;
	}
	
	function handleKeydown(e: KeyboardEvent) {
		// Ctrl/Cmd + Enter to execute
		if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
			e.preventDefault();
			executeQuery();
		}
	}
	
	function formatValue(value: any): string {
		if (value === null) return 'NULL';
		if (value === undefined) return '';
		if (typeof value === 'object') return JSON.stringify(value);
		return String(value);
	}
	
	function exportCsv() {
		if (results.length === 0) return;
		
		const header = columns.join(',');
		const rows = results.map(row => 
			columns.map(col => {
				const val = formatValue(row[col]);
				return val.includes(',') ? `"${val}"` : val;
			}).join(',')
		);
		
		const csv = [header, ...rows].join('\n');
		const blob = new Blob([csv], { type: 'text/csv' });
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = 'query_results.csv';
		a.click();
		URL.revokeObjectURL(url);
	}
</script>

<svelte:head>
	<title>PostgreSQL Console | NAIA</title>
</svelte:head>

<div class="space-y-4">
	<!-- Header -->
	<div class="flex items-center justify-between">
		<div class="flex items-center gap-3">
			<span class="text-2xl">🐘</span>
			<div>
				<h1 class="text-xl font-bold text-gray-900 dark:text-gray-100">PostgreSQL Console</h1>
				<p class="text-sm text-gray-500">Query the NAIA metadata database</p>
			</div>
		</div>
		{#if $isMasterMode}
			<span class="px-2 py-1 bg-amber-900/30 text-amber-400 text-xs rounded border border-amber-800">
				Master Mode - Write Enabled
			</span>
		{/if}
	</div>
	
	<!-- Quick Queries -->
	<div class="flex flex-wrap gap-2">
		{#each savedQueries as sq}
			<button
				onclick={() => loadQuery(sq.query)}
				class="px-3 py-1.5 text-xs bg-gray-800 hover:bg-gray-700 text-gray-300 rounded-lg transition-colors"
			>
				{sq.name}
			</button>
		{/each}
	</div>
	
	<!-- Query Editor -->
	<div class="bg-gray-900 rounded-lg border border-gray-800 overflow-hidden">
		<div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 bg-gray-800/50">
			<span class="text-sm text-gray-400">SQL Query</span>
			<div class="flex items-center gap-2">
				<span class="text-xs text-gray-500">Ctrl+Enter to run</span>
				<button
					onclick={executeQuery}
					disabled={isLoading}
					class="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-500 disabled:bg-gray-700 text-white text-sm font-medium rounded transition-colors"
				>
					{isLoading ? 'Running...' : '▶ Run'}
				</button>
			</div>
		</div>
		<textarea
			bind:value={query}
			onkeydown={handleKeydown}
			rows="8"
			spellcheck="false"
			class="w-full px-4 py-3 bg-gray-900 text-gray-100 font-mono text-sm resize-y focus:outline-none"
			placeholder="Enter SQL query..."
		></textarea>
	</div>
	
	<!-- Results -->
	<div class="bg-gray-900 rounded-lg border border-gray-800 overflow-hidden">
		<div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 bg-gray-800/50">
			<div class="flex items-center gap-4">
				<span class="text-sm text-gray-400">Results</span>
				{#if executionTime !== null}
					<span class="text-xs text-gray-500">
						{rowCount} rows in {executionTime.toFixed(0)}ms
					</span>
				{/if}
			</div>
			{#if results.length > 0}
				<button
					onclick={exportCsv}
					class="px-3 py-1 text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded transition-colors"
				>
					📥 Export CSV
				</button>
			{/if}
		</div>
		
		{#if error}
			<div class="p-4 bg-red-900/20 border-b border-red-800">
				<div class="flex items-start gap-2 text-red-400">
					<span>❌</span>
					<pre class="text-sm whitespace-pre-wrap font-mono">{error}</pre>
				</div>
			</div>
		{/if}
		
		{#if isLoading}
			<div class="p-8 text-center text-gray-500">
				<div class="animate-spin text-2xl mb-2">⚙️</div>
				<p>Executing query...</p>
			</div>
		{:else if results.length > 0}
			<div class="overflow-x-auto max-h-[500px] overflow-y-auto">
				<table class="w-full text-sm">
					<thead class="bg-gray-800 sticky top-0">
						<tr>
							{#each columns as col}
								<th class="px-4 py-2 text-left text-gray-400 font-medium border-b border-gray-700">
									{col}
								</th>
							{/each}
						</tr>
					</thead>
					<tbody>
						{#each results as row, i}
							<tr class="border-b border-gray-800 hover:bg-gray-800/50">
								{#each columns as col}
									<td class="px-4 py-2 text-gray-300 font-mono text-xs max-w-xs truncate" title={formatValue(row[col])}>
										{#if row[col] === null}
											<span class="text-gray-600 italic">NULL</span>
										{:else}
											{formatValue(row[col])}
										{/if}
									</td>
								{/each}
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		{:else if !error}
			<div class="p-8 text-center text-gray-500">
				<p>Run a query to see results</p>
			</div>
		{/if}
	</div>
	
	<!-- Schema Reference -->
	<details class="bg-gray-900 rounded-lg border border-gray-800">
		<summary class="px-4 py-3 cursor-pointer text-gray-400 hover:text-gray-300">
			📋 Quick Schema Reference
		</summary>
		<div class="px-4 pb-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 text-xs font-mono">
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">points</div>
				<div class="text-gray-500 space-y-1">
					<div>id, tag_name, description</div>
					<div>eng_units, data_source_id</div>
					<div>is_enabled, created_at</div>
				</div>
			</div>
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">data_sources</div>
				<div class="text-gray-500 space-y-1">
					<div>id, name, type</div>
					<div>connection_string, status</div>
					<div>created_at, updated_at</div>
				</div>
			</div>
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">patterns</div>
				<div class="text-gray-500 space-y-1">
					<div>id, name, description</div>
					<div>confidence, match_count</div>
					<div>is_active, created_at</div>
				</div>
			</div>
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">suggestions</div>
				<div class="text-gray-500 space-y-1">
					<div>id, pattern_id, confidence</div>
					<div>status, reviewed_by</div>
					<div>created_at, reviewed_at</div>
				</div>
			</div>
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">elements</div>
				<div class="text-gray-500 space-y-1">
					<div>id, name, path</div>
					<div>element_type, parent_id</div>
					<div>created_at</div>
				</div>
			</div>
			<div class="p-3 bg-gray-800/50 rounded">
				<div class="text-emerald-400 font-semibold mb-2">logs</div>
				<div class="text-gray-500 space-y-1">
					<div>id, timestamp, level</div>
					<div>message, exception</div>
					<div>source_context</div>
				</div>
			</div>
		</div>
	</details>
</div>
