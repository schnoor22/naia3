const API_BASE = '';

export interface ApiResponse<T> {
	success: boolean;
	data?: T;
	error?: string;
}

export interface PaginatedResponse<T> {
	data: T[];
	total: number;
	skip: number;
	take: number;
}

async function fetchApi<T>(endpoint: string, options?: RequestInit): Promise<T> {
	const response = await fetch(`${API_BASE}${endpoint}`, {
		...options,
		headers: {
			'Content-Type': 'application/json',
			...options?.headers,
		},
	});

	if (!response.ok) {
		const errorText = await response.text();
		throw new Error(errorText || `HTTP ${response.status}`);
	}

	return response.json();
}

// Health endpoints
export interface HealthStatus {
	status: 'healthy' | 'degraded' | 'unhealthy';
	checks: Record<string, { status: string; error?: string }>;
	timestamp: string;
}

export async function getHealth(): Promise<HealthStatus> {
	return fetchApi<HealthStatus>('/api/health');
}

// Pipeline endpoints
export interface PipelineMetrics {
	isRunning: boolean;
	pointsPerSecond: number;
	totalPointsIngested: number;
	batchesProcessed: number;
	errors: number;
	lastUpdateTime: string;
}

export async function getPipelineMetrics(): Promise<PipelineMetrics> {
	return fetchApi<PipelineMetrics>('/api/pipeline/metrics');
}

export async function getPipelineHealth(): Promise<any> {
	return fetchApi('/api/pipeline/health');
}

// Ingestion control
export interface IngestionStatus {
	isRunning: boolean;
	pointsConfigured: number;
	pollInterval: number;
	lastPollTime?: string;
	messagesPublished: number;
	errors: number;
}

export async function getIngestionStatus(): Promise<IngestionStatus> {
	return fetchApi<IngestionStatus>('/api/ingestion/status');
}

export async function startIngestion(): Promise<any> {
	return fetchApi('/api/ingestion/start', { method: 'POST' });
}

export async function stopIngestion(): Promise<any> {
	return fetchApi('/api/ingestion/stop', { method: 'POST' });
}

// Data sources
export interface DataSource {
	id: string;
	name: string;
	type: string;
	connectionString?: string;
	status: string;
	lastConnected?: string;
}

export async function getDataSources(): Promise<DataSource[]> {
	return fetchApi<DataSource[]>('/api/datasources');
}

// Points
export interface Point {
	id: string;
	pointSequenceId?: number;
	name: string;
	description?: string;
	engineeringUnits?: string;
	valueType: string;
	kind: string;
	sourceAddress: string;
	dataSourceId: string;
	dataSourceName?: string;
	isEnabled: boolean;
	createdAt: string;
	updatedAt?: string;
}

export interface PointsSearchParams {
	tagName?: string;
	dataSourceId?: string;
	enabled?: boolean;
	skip?: number;
	take?: number;
}

export async function searchPoints(params: PointsSearchParams = {}): Promise<PaginatedResponse<Point>> {
	const searchParams = new URLSearchParams();
	if (params.tagName) searchParams.set('tagName', params.tagName);
	if (params.dataSourceId) searchParams.set('dataSourceId', params.dataSourceId);
	if (params.enabled !== undefined) searchParams.set('enabled', String(params.enabled));
	if (params.skip !== undefined) searchParams.set('skip', String(params.skip));
	if (params.take !== undefined) searchParams.set('take', String(params.take));

	const query = searchParams.toString();
	return fetchApi<PaginatedResponse<Point>>(`/api/points${query ? `?${query}` : ''}`);
}

export async function getPoint(id: string): Promise<Point> {
	return fetchApi<Point>(`/api/points/${id}`);
}

// Current value
export interface CurrentValue {
	value: number | string | boolean;
	timestamp: string;
	quality: string;
}

export async function getCurrentValue(pointId: string): Promise<CurrentValue> {
	return fetchApi<CurrentValue>(`/api/points/${pointId}/current`);
}

// Historical data
export interface HistoricalDataPoint {
	timestamp: string;
	value: number;
	quality: string;
}

export interface HistoricalDataResponse {
	pointId: string;
	tagName: string;
	start: string;
	end: string;
	count: number;
	data: HistoricalDataPoint[];
}

export async function getHistory(
	pointId: string,
	start?: Date,
	end?: Date,
	limit?: number
): Promise<HistoricalDataResponse> {
	const params = new URLSearchParams();
	if (start) params.set('start', start.toISOString());
	if (end) params.set('end', end.toISOString());
	if (limit) params.set('limit', String(limit));

	const query = params.toString();
	return fetchApi<HistoricalDataResponse>(`/api/points/${pointId}/history${query ? `?${query}` : ''}`);
}

// Suggestions
export interface Suggestion {
	id: string;
	clusterId: string;
	patternId: string;
	patternName: string;
	confidence: number;
	pointCount: number;
	status: number; // 0=Pending, 1=Approved, 2=Rejected, 3=Deferred, 4=Expired
	createdAt: string;
	commonPrefix?: string;
}

export interface SuggestionDetail extends Suggestion {
	namingScore: number;
	correlationScore: number;
	rangeScore: number;
	rateScore: number;
	reason: string;
	points: {
		pointId: string;
		pointName: string;
		suggestedRole: string | null;
		roleConfidence: number | null;
	}[];
	expectedRoles: {
		id: string;
		name: string;
		description: string;
		namingPatterns: string[];
		expectedMinValue?: number;
		expectedMaxValue?: number;
		expectedUnits?: string;
		isRequired: boolean;
	}[];
}

export interface SuggestionStats {
	pending: number;
	approvedToday: number;
	rejectedToday: number;
	totalApproved: number;
	totalRejected: number;
	averageConfidence: number;
}

export async function getPendingSuggestions(skip = 0, take = 50): Promise<PaginatedResponse<Suggestion>> {
	return fetchApi<PaginatedResponse<Suggestion>>(`/api/suggestions?skip=${skip}&take=${take}`);
}

export async function getSuggestion(id: string): Promise<SuggestionDetail> {
	return fetchApi<SuggestionDetail>(`/api/suggestions/${id}`);
}

export async function getSuggestionStats(): Promise<SuggestionStats> {
	return fetchApi<SuggestionStats>('/api/suggestions/stats');
}

export async function approveSuggestion(id: string, userId?: string): Promise<any> {
	return fetchApi(`/api/suggestions/${id}/approve`, {
		method: 'POST',
		body: JSON.stringify({ userId }),
	});
}

export async function rejectSuggestion(id: string, reason?: string, userId?: string): Promise<any> {
	return fetchApi(`/api/suggestions/${id}/reject`, {
		method: 'POST',
		body: JSON.stringify({ reason, userId }),
	});
}

export async function deferSuggestion(id: string): Promise<any> {
	return fetchApi(`/api/suggestions/${id}/defer`, { method: 'POST' });
}

// Patterns
export interface Pattern {
	id: string;
	name: string;
	description?: string;
	category: string;
	confidence: number;
	approvalCount: number;
	rejectionCount: number;
	createdAt: string;
	updatedAt?: string;
}

export async function getPatterns(): Promise<Pattern[]> {
	return fetchApi<Pattern[]>('/api/patterns');
}

export async function getPatternStats(): Promise<any> {
	return fetchApi('/api/patterns/stats');
}

// PI System
export async function checkPIHealth(): Promise<any> {
	return fetchApi('/api/pi/health');
}

export async function discoverPIPoints(filter?: string, maxResults?: number): Promise<any> {
	const params = new URLSearchParams();
	if (filter) params.set('filter', filter);
	if (maxResults) params.set('maxResults', String(maxResults));
	const query = params.toString();
	return fetchApi(`/api/pi/points${query ? `?${query}` : ''}`);
}
export async function addPIPoints(points: any[]): Promise<any> {
	return fetchApi('/api/pi/points/add', {
		method: 'POST',
		body: JSON.stringify({ points })
	});
}