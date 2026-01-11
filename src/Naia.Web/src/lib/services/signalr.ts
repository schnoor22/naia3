import * as signalR from '@microsoft/signalr';
import { connectionState, pendingCount, toasts } from '../stores/signalr';

let connection: signalR.HubConnection | null = null;

export function initializeSignalR(): void {
	if (connection) return;

	connection = new signalR.HubConnectionBuilder()
		.withUrl('/hubs/patterns')
		.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
		.configureLogging(signalR.LogLevel.Information)
		.build();

	// Connection state handlers
	connection.onreconnecting(() => {
		connectionState.set('reconnecting');
	});

	connection.onreconnected(() => {
		connectionState.set('connected');
		toasts.add({
			type: 'success',
			title: 'Reconnected',
			message: 'Real-time connection restored'
		});
	});

	connection.onclose(() => {
		connectionState.set('disconnected');
	});

	// Pattern hub event handlers
	connection.on('PendingCount', (count: number) => {
		pendingCount.set(count);
	});

	connection.on('SuggestionCreated', (suggestion: { id: string; patternName: string; confidence: number }) => {
		toasts.add({
			type: 'info',
			title: 'New Pattern Suggestion',
			message: `${suggestion.patternName} (${Math.round(suggestion.confidence * 100)}% confidence)`
		});
		// Increment pending count
		pendingCount.update((n) => n + 1);
	});

	connection.on('SuggestionApproved', (data: { suggestionId: string; patternName: string }) => {
		toasts.add({
			type: 'success',
			title: 'Pattern Approved',
			message: `${data.patternName} - NAIA is learning!`
		});
	});

	connection.on('PatternUpdated', (pattern: { name: string; confidence: number }) => {
		toasts.add({
			type: 'info',
			title: 'Pattern Updated',
			message: `${pattern.name} confidence: ${Math.round(pattern.confidence * 100)}%`
		});
	});

	connection.on('ClusterDetected', (cluster: { clusterId: string; pointCount: number; commonPrefix: string }) => {
		toasts.add({
			type: 'info',
			title: 'Cluster Detected',
			message: `${cluster.pointCount} points with prefix "${cluster.commonPrefix}"`
		});
	});

	// Start connection
	startConnection();
}

async function startConnection(): Promise<void> {
	if (!connection) return;

	connectionState.set('connecting');

	try {
		await connection.start();
		connectionState.set('connected');
		
		// Subscribe to all patterns for admin dashboard
		await connection.invoke('SubscribeToAllPatterns');
	} catch (error) {
		console.error('SignalR connection failed:', error);
		connectionState.set('disconnected');
		
		// Retry after delay
		setTimeout(startConnection, 5000);
	}
}

export function getConnection(): signalR.HubConnection | null {
	return connection;
}
