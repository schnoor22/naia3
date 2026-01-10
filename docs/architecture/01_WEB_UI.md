# Web UI - SvelteKit 2 + Svelte 5

**Component:** Frontend Layer  
**Technology:** SvelteKit 2, Svelte 5, TypeScript, TailwindCSS  
**Port:** 5173 (dev), 5282 (production proxied via API)  
**Status:** ✅ Fully Operational

---

## 🎯 Role in NAIA Architecture

The Web UI is the **primary user interface** for NAIA, providing engineers and operators with tools to:
- Browse and organize industrial asset hierarchies
- Review AI-generated pattern suggestions (The Flywheel)
- Monitor real-time data from thousands of sensors
- Configure data sources and manage system health

**In the context of the vision:** This is where humans interact with the intelligence loop. User approvals here feed back into the pattern learning system, making NAIA smarter over time.

---

## 🏗️ Architecture

### Tech Stack

```
┌─────────────────────────────────────────────────────────┐
│                    Browser (Chrome/Edge)                │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              SvelteKit 2 (Framework)                    │
│  • File-based routing (+page.svelte)                   │
│  • Server-side rendering (SSR) + client hydration      │
│  • Load functions for data fetching                    │
│  • Form actions for mutations                          │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│               Svelte 5 Runes (Reactivity)               │
│  • $state: Reactive variables                          │
│  • $derived: Computed values                           │
│  • $effect: Side effects                               │
│  • Compiler optimizes to vanilla JS                    │
└────────────────────────┬────────────────────────────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
┌────────▼─────┐ ┌──────▼──────┐ ┌─────▼────────┐
│  TanStack    │ │   SignalR   │ │  TailwindCSS │
│  Query       │ │   Client    │ │  + Lucide    │
│              │ │             │ │              │
│ • Caching    │ │ • WebSocket │ │ • Utility-   │
│ • Refetch    │ │ • Push      │ │   first CSS  │
│ • Invalidate │ │ • Reconnect │ │ • Icons      │
└──────┬───────┘ └──────┬──────┘ └──────────────┘
       │                │
┌──────▼────────────────▼──────────────────────────────┐
│            REST API + SignalR (Port 5282)            │
│  • Fetch API (HTTP requests)                         │
│  • @microsoft/signalr (WebSocket)                    │
└──────────────────────────────────────────────────────┘
```

### Key Features

1. **Reactive State Management**
   - Svelte 5 runes eliminate prop drilling
   - TanStack Query handles server state
   - Automatic UI updates on data changes

2. **Real-Time Updates**
   - SignalR hub connections
   - Live dashboard metrics
   - OPC UA discovery notifications

3. **Responsive Design**
   - Mobile-first TailwindCSS
   - Adaptive layouts for desktop/tablet/phone
   - Dark mode support (planned)

---

## 📂 Project Structure

```
naia-ui/
├── src/
│   ├── routes/                    # File-based routing
│   │   ├── +layout.svelte         # Root layout (nav, auth)
│   │   ├── +page.svelte           # Home/dashboard
│   │   ├── framework/             # Asset hierarchy builder
│   │   │   ├── +page.svelte       # Main tree view
│   │   │   └── organize/          # Organize sub-route
│   │   ├── patterns/              # Pattern library browser
│   │   ├── review-suggestions/    # Flywheel approval UI ⭐
│   │   ├── admin/
│   │   │   ├── data-sources/      # OPC UA, PI connectors
│   │   │   ├── points/            # Point CRUD
│   │   │   └── monitoring/        # System health
│   │   └── dashboard/             # Real-time metrics
│   ├── lib/
│   │   ├── api/                   # API client functions
│   │   │   ├── elements.ts        # Elements CRUD
│   │   │   ├── patterns.ts        # Patterns API
│   │   │   ├── dataSources.ts     # Data sources API
│   │   │   └── signalr.ts         # SignalR hub setup
│   │   ├── components/            # Reusable UI components
│   │   │   ├── ElementTree.svelte # Hierarchy tree
│   │   │   ├── PatternCard.svelte # Pattern display
│   │   │   └── DataGrid.svelte    # Generic grid
│   │   ├── stores/                # Client-side state
│   │   └── utils/                 # Helper functions
│   └── app.html                   # HTML template
├── static/                        # Static assets
├── vite.config.ts                 # Vite bundler config
├── tailwind.config.js             # TailwindCSS config
└── package.json                   # Dependencies
```

---

## 🔄 Data Flow Pattern

### Example: Loading Elements

```typescript
// 1. Route loads data (src/routes/framework/+page.ts)
export async function load({ fetch }) {
  const response = await fetch('/api/elements');
  return { elements: await response.json() };
}

// 2. Component receives data (+page.svelte)
<script lang="ts">
  let { data } = $props(); // Svelte 5 rune
  let elements = $state(data.elements);
  
  // TanStack Query for real-time updates
  const query = createQuery({
    queryKey: ['elements'],
    queryFn: () => fetch('/api/elements').then(r => r.json()),
    refetchInterval: 10000 // Refetch every 10s
  });
</script>

// 3. SignalR pushes updates
<script>
  onMount(() => {
    signalRConnection.on('ElementCreated', (newElement) => {
      elements = [...elements, newElement]; // Svelte reactivity
    });
  });
</script>

// 4. UI auto-updates
<ElementTree {elements} />
```

### Real-Time Dashboard Flow

```
User opens /dashboard
  └─> Load function fetches initial metrics
  └─> Component mounts, starts SignalR connection
  └─> DataHub.on('MetricsUpdate', (data) => {...})
  └─> UI updates every second with live point counts
```

---

## 🎨 Key Routes

### 1. `/framework` - Asset Hierarchy Builder
**Purpose:** Visual tree interface for organizing industrial assets  
**Features:**
- Drag-and-drop element organization
- Create/edit/delete elements
- Bind points to elements
- Template-based creation

**In the vision:** This is where engineers structure their plant. The better organized, the more effective the pattern matching.

---

### 2. `/review-suggestions` - The Flywheel Core ⭐
**Purpose:** Review and approve AI-generated pattern suggestions  
**Features:**
- List suggested elements with confidence scores
- Preview pattern details and matched points
- Approve/reject suggestions
- See learning feedback

**In the vision:** **This is the heart of NAIA.** User approvals here train the system, increasing future confidence. The flywheel spins faster with each approval.

**UI Flow:**
```
1. Background job creates suggestions → PostgreSQL
2. UI fetches suggestions via GET /api/suggestions
3. User clicks suggestion → Shows details modal
4. User clicks "Approve" → POST /api/suggestions/{id}/approve
5. Backend creates element, updates pattern confidence
6. UI shows success, removes suggestion from list
7. Next time similar pattern appears, confidence is higher
```

---

### 3. `/admin/data-sources` - Connector Configuration
**Purpose:** Manage connections to external systems  
**Features:**
- Add OPC UA servers (endpoint URL)
- Configure PI Web API (server, authentication)
- Test connections
- View connection health

**In the vision:** Data sources are the input to the flywheel. More sources = more data = better learning.

---

### 4. `/admin/monitoring` - System Health
**Purpose:** Monitor NAIA's operational status  
**Features:**
- QuestDB metrics (insert rate, partition count)
- SignalR connection status
- Background job execution logs
- API health checks

**In the vision:** Observability ensures the flywheel keeps spinning. Alerts prevent data gaps.

---

### 5. `/patterns` - Pattern Library Browser
**Purpose:** Browse and manage pattern definitions  
**Features:**
- Search pattern templates
- View pattern attributes
- Edit pattern fingerprints
- See usage statistics (how many elements use this pattern)

**In the vision:** The pattern library is the "knowledge base" that grows smarter. Each approved suggestion strengthens these patterns.

---

### 6. `/dashboard` - Real-Time Metrics
**Purpose:** Live operational overview  
**Features:**
- Total point count
- Current values streaming rate
- Recent elements created
- Suggestion approval rate

**In the vision:** Gamification - show users the impact of their approvals. "You've improved pattern confidence by 12% this week!"

---

## 🔌 API Integration

### REST API Client

```typescript
// src/lib/api/elements.ts
export async function getElements(): Promise<Element[]> {
  const response = await fetch('/api/elements', {
    headers: { 'Authorization': `Bearer ${getToken()}` }
  });
  if (!response.ok) throw new Error('Failed to fetch elements');
  return response.json();
}

export async function createElement(element: CreateElementDto): Promise<Element> {
  const response = await fetch('/api/elements', {
    method: 'POST',
    headers: { 
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${getToken()}`
    },
    body: JSON.stringify(element)
  });
  return response.json();
}
```

### SignalR Integration

```typescript
// src/lib/api/signalr.ts
import { HubConnectionBuilder } from '@microsoft/signalr';

export function createDataHub() {
  return new HubConnectionBuilder()
    .withUrl('http://localhost:5282/hubs/data')
    .withAutomaticReconnect()
    .build();
}

// Usage in component
onMount(async () => {
  const hub = createDataHub();
  await hub.start();
  
  hub.on('CurrentValuesUpdate', (points) => {
    currentValues = points; // Svelte reactivity
  });
  
  return () => hub.stop();
});
```

---

## 🎯 Design Principles

### 1. **Progressive Enhancement**
- Server-side rendering ensures fast initial load
- Client-side hydration adds interactivity
- Works with JavaScript disabled (basic functionality)

### 2. **Optimistic Updates**
- UI updates immediately on user action
- Reverts if server request fails
- TanStack Query handles rollback

### 3. **Accessibility**
- Semantic HTML (`<nav>`, `<main>`, `<section>`)
- ARIA labels for screen readers
- Keyboard navigation support

### 4. **Performance**
- Lazy-loaded routes (code splitting)
- Virtual scrolling for large lists (1000+ elements)
- Debounced search inputs

---

## 🚀 Development Workflow

### Local Development
```bash
cd naia-ui
npm install
npm run dev  # Starts Vite dev server on port 5173
```

### Build for Production
```bash
npm run build  # Creates optimized bundle in .svelte-kit/
npm run preview  # Test production build locally
```

### Deployment
- Production build served by .NET API at `/` route
- Static assets in `wwwroot/`
- SPA fallback for client-side routing

---

## 📊 Current Status

### ✅ Implemented Routes
- `/` - Dashboard with live metrics
- `/framework` - Full hierarchy builder
- `/framework/organize` - Element organization UI
- `/patterns` - Pattern library browser
- `/review-suggestions` - Flywheel approval workflow ⭐
- `/admin/data-sources` - Connector management
- `/admin/points` - Point CRUD
- `/admin/monitoring` - Health dashboard

### 🚧 In Progress
- Dark mode toggle
- Mobile responsive improvements
- Accessibility audit

### 📋 Planned
- `/personas` - User role management
- `/ai-assistant` - Coral AI chat interface
- `/exports` - Data export wizard (Excel, Power BI)
- `/settings` - User preferences, AI tuning

---

## 🔗 Dependencies

```json
{
  "dependencies": {
    "@sveltejs/kit": "^2.0.0",
    "svelte": "^5.0.0",
    "@tanstack/svelte-query": "^5.0.0",
    "@microsoft/signalr": "^8.0.0",
    "tailwindcss": "^3.4.0",
    "lucide-svelte": "^0.300.0",
    "typescript": "^5.3.0"
  }
}
```

---

## 🤝 Integration Points

### With REST API
- **Endpoints:** All `/api/*` routes
- **Auth:** JWT Bearer tokens (future)
- **CORS:** Configured for `localhost:5173`

### With SignalR
- **Hubs:** DataHub, DiscoveryHub, SmartRelayHub
- **Events:** Real-time push notifications
- **Reconnect:** Automatic with exponential backoff

### With QuestDB
- **Direct queries:** None (API abstraction layer)
- **Read-only:** Future admin query interface

---

## 📈 Performance Metrics

- **Initial Load:** < 2s (SSR + hydration)
- **Route Transition:** < 200ms
- **Real-time Update Latency:** < 50ms (SignalR)
- **Bundle Size:** ~150 KB gzipped

---

**Next:** [REST API Documentation](./02_REST_API.md)
