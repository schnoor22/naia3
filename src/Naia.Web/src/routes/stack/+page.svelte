<script lang="ts">
	const technologies = [
		{
			name: 'PostgreSQL 16',
			category: 'Configuration & Metadata',
			purpose: 'Stores point definitions, data sources, patterns, and system configuration',
			throughput: '15,000+ writes/sec on standard hardware',
			why: 'Battle-tested RDBMS with excellent JSONB support for flexible configuration'
		},
		{
			name: 'QuestDB',
			category: 'Time-Series Storage',
			purpose: 'High-performance columnar storage for billions of data points',
			throughput: '4M+ rows/sec ingestion, sub-millisecond queries',
			why: 'Purpose-built for time-series with SQL compatibility and blazing fast aggregations'
		},
		{
			name: 'Redis 7',
			category: 'Real-Time Cache',
			purpose: 'Caches current values and provides idempotency for exactly-once processing',
			throughput: '100,000+ ops/sec per instance',
			why: 'In-memory speed for instant access to latest sensor readings'
		},
		{
			name: 'Apache Kafka',
			category: 'Message Streaming',
			purpose: 'Decouples data ingestion from storage with durable message queue',
			throughput: '1M+ messages/sec per broker',
			why: 'Ensures zero data loss with at-least-once delivery guarantees'
		},
		{
			name: '.NET 8',
			category: 'Application Runtime',
			purpose: 'Powers the API and ingestion workers with modern async/await patterns',
			throughput: '10M+ requests/sec (TechEmpower benchmarks)',
			why: 'Cross-platform, high-performance runtime with excellent tooling'
		},
		{
			name: 'SvelteKit',
			category: 'Web Framework',
			purpose: 'Delivers responsive, real-time UI with minimal JavaScript payload',
			throughput: 'Sub-50ms time-to-interactive',
			why: 'Compiles to vanilla JS for maximum performance and developer experience'
		},
		{
			name: 'OSIsoft PI',
			category: 'Industrial Data Source',
			purpose: 'Connects to existing PI historians via Web API and AF SDK',
			throughput: 'Limited by PI server capacity',
			why: 'Industry standard for process data in manufacturing and energy'
		},
		{
			name: 'OPC UA',
			category: 'Industrial Data Source',
			purpose: 'Universal connectivity to PLCs, DCS, and industrial equipment',
			throughput: 'Protocol-dependent, typically 10K-100K updates/sec',
			why: 'Open standard for industrial automation with built-in security'
		},
		{
			name: 'Modbus TCP/RTU',
			category: 'Industrial Data Source',
			purpose: 'Direct connection to legacy industrial devices and sensors',
			throughput: '1-10K registers/sec per device',
			why: 'Ubiquitous protocol in industrial environments, simple integration'
		},
		{
			name: 'CSV & Flat Files',
			category: 'Industrial Data Source',
			purpose: 'Import historical data and batch uploads from any system',
			throughput: 'Disk I/O limited, millions of rows per file',
			why: 'Universal data exchange format, enables migration from any historian'
		},
		{
			name: 'Docker',
			category: 'Infrastructure',
			purpose: 'Containerizes all dependencies for consistent deployment',
			throughput: 'Near-native performance',
			why: 'Simplified operations with reproducible environments'
		}
	];

	const categories = [...new Set(technologies.map((t) => t.category))];

	function getTechsByCategory(category: string) {
		return technologies.filter((t) => t.category === category);
	}
</script>

<svelte:head>
	<title>Technology Stack - NAIA</title>
</svelte:head>

<div class="stack-page">
	<header class="header">
		<h1>Technology Stack</h1>
		<p class="subtitle">
			NAIA combines best-in-class open source and enterprise technologies for industrial-scale
			data processing
		</p>
	</header>

	<div class="categories">
		{#each categories as category}
			<section class="category">
				<h2>{category}</h2>
				<div class="tech-grid">
					{#each getTechsByCategory(category) as tech}
						<article class="tech-card">
							<header class="tech-header">
								<h3>{tech.name}</h3>
								<span class="throughput">{tech.throughput}</span>
							</header>
							<p class="purpose">{tech.purpose}</p>
							<footer class="why">
								<strong>Why:</strong>
								{tech.why}
							</footer>
						</article>
					{/each}
				</div>
			</section>
		{/each}
	</div>

	<section class="architecture">
		<h2>Data Flow Architecture</h2>
		<div class="flow-diagram">
			<div class="flow-step">
				<div class="step-number">1</div>
				<h3>Ingestion</h3>
				<p>Any Data Source → Kafka</p>
				<div style="font-size: 0.7rem; color: var(--color-text-secondary); margin-top: 0.25rem;">PI • OPC UA • Modbus • CSV</div>
			</div>
			<div class="flow-arrow">→</div>
			<div class="flow-step">
				<div class="step-number">2</div>
				<h3>Processing</h3>
				<p>Deduplication + Validation</p>
			</div>
			<div class="flow-arrow">→</div>
			<div class="flow-step">
				<div class="step-number">3</div>
				<h3>Storage</h3>
				<p>QuestDB + Redis Cache</p>
			</div>
			<div class="flow-arrow">→</div>
			<div class="flow-step">
				<div class="step-number">4</div>
				<h3>Analysis</h3>
				<p>Pattern Learning + Alerts</p>
			</div>
		</div>
	</section>

	<section class="metrics">
		<h2>System Capacity</h2>
		<div class="metric-grid">
			<div class="metric-card">
				<div class="metric-value">100M+</div>
				<div class="metric-label">Data Points / Day</div>
			</div>
			<div class="metric-card">
				<div class="metric-value">1M+</div>
				<div class="metric-label">Tags / Instance</div>
			</div>
			<div class="metric-card">
				<div class="metric-value">&lt;10ms</div>
				<div class="metric-label">Write Latency</div>
			</div>
			<div class="metric-card">
				<div class="metric-value">&lt;50ms</div>
				<div class="metric-label">Query Response</div>
			</div>
		</div>
	</section>
</div>

<style>
	.stack-page {
		padding: 2rem;
		max-width: 1400px;
		margin: 0 auto;
	}

	.header {
		text-align: center;
		margin-bottom: 3rem;
	}

	.header h1 {
		font-size: 2.5rem;
		font-weight: 700;
		margin-bottom: 0.5rem;
		color: var(--color-text-primary);
	}

	.subtitle {
		font-size: 1.125rem;
		color: var(--color-text-secondary);
		max-width: 800px;
		margin: 0 auto;
	}

	.categories {
		display: flex;
		flex-direction: column;
		gap: 3rem;
	}

	.category h2 {
		font-size: 1.5rem;
		font-weight: 600;
		color: var(--color-primary);
		margin-bottom: 1rem;
		padding-bottom: 0.5rem;
		border-bottom: 2px solid var(--color-border);
	}

	.tech-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
		gap: 1.5rem;
	}

	.tech-card {
		background: var(--color-surface);
		border: 1px solid var(--color-border);
		border-radius: 8px;
		padding: 1.5rem;
		transition: all 0.2s ease;
	}

	.tech-card:hover {
		border-color: var(--color-primary);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
		transform: translateY(-2px);
	}

	.tech-header {
		display: flex;
		justify-content: space-between;
		align-items: start;
		margin-bottom: 0.75rem;
		gap: 1rem;
	}

	.tech-header h3 {
		font-size: 1.25rem;
		font-weight: 600;
		color: var(--color-text-primary);
		margin: 0;
	}

	.throughput {
		font-size: 0.75rem;
		color: var(--color-success);
		background: rgba(34, 197, 94, 0.1);
		padding: 0.25rem 0.5rem;
		border-radius: 4px;
		white-space: nowrap;
		font-weight: 500;
	}

	.purpose {
		font-size: 0.9375rem;
		color: var(--color-text-secondary);
		margin-bottom: 0.75rem;
		line-height: 1.5;
	}

	.why {
		font-size: 0.875rem;
		color: var(--color-text-tertiary);
		padding-top: 0.75rem;
		border-top: 1px solid var(--color-border-subtle);
		line-height: 1.4;
	}

	.why strong {
		color: var(--color-text-secondary);
	}

	.architecture {
		margin-top: 4rem;
		padding: 2rem;
		background: var(--color-surface);
		border-radius: 12px;
		border: 1px solid var(--color-border);
	}

	.architecture h2 {
		font-size: 1.5rem;
		font-weight: 600;
		color: var(--color-primary);
		margin-bottom: 2rem;
		text-align: center;
	}

	.flow-diagram {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 1rem;
		flex-wrap: wrap;
	}

	.flow-step {
		flex: 1;
		min-width: 160px;
		max-width: 200px;
		text-align: center;
		padding: 1.5rem;
		background: linear-gradient(135deg, var(--color-primary-subtle) 0%, var(--color-surface) 100%);
		border: 2px solid var(--color-primary);
		border-radius: 8px;
		position: relative;
	}

	.step-number {
		position: absolute;
		top: -12px;
		left: 50%;
		transform: translateX(-50%);
		width: 32px;
		height: 32px;
		background: var(--color-primary);
		color: white;
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		font-weight: 700;
		font-size: 0.875rem;
	}

	.flow-step h3 {
		font-size: 1.125rem;
		font-weight: 600;
		color: var(--color-text-primary);
		margin-bottom: 0.5rem;
	}

	.flow-step p {
		font-size: 0.875rem;
		color: var(--color-text-secondary);
		margin: 0;
	}

	.flow-arrow {
		font-size: 2rem;
		color: var(--color-primary);
		font-weight: 300;
		flex-shrink: 0;
	}

	.metrics {
		margin-top: 3rem;
	}

	.metrics h2 {
		font-size: 1.5rem;
		font-weight: 600;
		color: var(--color-primary);
		margin-bottom: 1.5rem;
		text-align: center;
	}

	.metric-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
		gap: 1.5rem;
	}

	.metric-card {
		background: var(--color-surface);
		border: 1px solid var(--color-border);
		border-radius: 8px;
		padding: 2rem;
		text-align: center;
		transition: all 0.2s ease;
	}

	.metric-card:hover {
		border-color: var(--color-primary);
		transform: translateY(-2px);
	}

	.metric-value {
		font-size: 2.5rem;
		font-weight: 700;
		color: var(--color-primary);
		margin-bottom: 0.5rem;
	}

	.metric-label {
		font-size: 0.875rem;
		color: var(--color-text-secondary);
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	@media (max-width: 768px) {
		.flow-diagram {
			flex-direction: column;
		}

		.flow-arrow {
			transform: rotate(90deg);
		}

		.tech-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
