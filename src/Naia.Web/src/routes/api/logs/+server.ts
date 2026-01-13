import { json, type RequestHandler } from '@sveltejs/kit';

const BACKEND_URL = 'http://localhost:5052';

export const GET: RequestHandler = async ({ url }) => {
	try {
		// Build the backend URL with all query parameters
		const backendUrl = new URL('/api/logs', BACKEND_URL);
		
		// Copy all query parameters from the frontend request
		url.searchParams.forEach((value, key) => {
			backendUrl.searchParams.set(key, value);
		});
		
		console.log('Proxying logs request to:', backendUrl.toString());
		
		// Fetch from backend
		const response = await fetch(backendUrl.toString(), {
			method: 'GET',
			headers: {
				'Accept': 'application/json',
				'Content-Type': 'application/json'
			}
		});
		
		if (!response.ok) {
			const errorText = await response.text();
			console.error(`Backend logs endpoint failed: ${response.status}`, errorText);
			return json(
				{ error: `Backend error: ${response.status}` },
				{ status: response.status }
			);
		}
		
		const data = await response.json();
		console.log('Backend returned:', data.total, 'logs');
		return json(data);
	} catch (error) {
		const message = error instanceof Error ? error.message : 'Unknown error';
		console.error('Logs endpoint error:', message);
		return json(
			{ error: message },
			{ status: 500 }
		);
	}
};
