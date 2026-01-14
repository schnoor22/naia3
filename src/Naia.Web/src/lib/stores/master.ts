import { writable, derived, get } from 'svelte/store';

// Master token for elevated access
const MASTER_TOKEN_KEY = 'naia_master_token';

// Store for master token
function createMasterStore() {
    // Initialize from localStorage if available
    const storedToken = typeof window !== 'undefined' 
        ? localStorage.getItem(MASTER_TOKEN_KEY) 
        : null;
    
    const { subscribe, set, update } = writable<string | null>(storedToken);
    
    return {
        subscribe,
        
        setToken: (token: string) => {
            if (typeof window !== 'undefined') {
                localStorage.setItem(MASTER_TOKEN_KEY, token);
            }
            set(token);
        },
        
        clearToken: () => {
            if (typeof window !== 'undefined') {
                localStorage.removeItem(MASTER_TOKEN_KEY);
            }
            set(null);
        },
        
        getToken: () => {
            return typeof window !== 'undefined' 
                ? localStorage.getItem(MASTER_TOKEN_KEY) 
                : null;
        }
    };
}

export const masterToken = createMasterStore();

// Derived store for whether master mode is active
export const isMasterMode = derived(masterToken, $token => !!$token);

// Helper to get headers with master token
export function getMasterHeaders(): HeadersInit {
    const token = get(masterToken);
    if (!token) return {};
    return {
        'X-Master-Token': token
    };
}

// Verify master token with the API
export async function verifyMasterAccess(): Promise<boolean> {
    const token = get(masterToken);
    if (!token) return false;
    
    try {
        const response = await fetch('/api/debug/access', {
            headers: {
                'X-Master-Token': token
            }
        });
        
        if (response.ok) {
            const data = await response.json();
            return data.hasMasterAccess === true;
        }
        
        // Token is invalid, clear it
        masterToken.clearToken();
        return false;
    } catch {
        return false;
    }
}

// Attempt master login
export async function loginMaster(token: string): Promise<{ success: boolean; error?: string }> {
    try {
        const response = await fetch('/api/debug/access', {
            headers: {
                'X-Master-Token': token
            }
        });
        
        if (response.ok) {
            const data = await response.json();
            if (data.hasMasterAccess) {
                masterToken.setToken(token);
                return { success: true };
            }
        }
        
        return { success: false, error: 'Invalid master token' };
    } catch (e) {
        return { success: false, error: 'Connection error' };
    }
}

// Logout from master mode
export function logoutMaster() {
    masterToken.clearToken();
}
