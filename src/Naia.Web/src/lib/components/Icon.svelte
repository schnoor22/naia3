<script lang="ts">
import IconifyIcon from '@iconify/svelte';

interface Props {
name: string;
class?: string;
size?: string;
}

let { name, class: className = '', size = '20' }: Props = $props();

// Map semantic names to Mingcute icons
const iconMap: Record<string, string> = {
// Navigation
'dashboard': 'mingcute:dashboard-2-line',
'points': 'mingcute:list-check-line',
'trends': 'mingcute:chart-line-line',
'patterns': 'mingcute:sparkles-2-line',
'ingestion': 'mingcute:upload-2-line',
'stack': 'mingcute:layers-line',
'logs': 'mingcute:file-line',

// Status & Actions
'settings': 'mingcute:settings-3-line',
'check': 'mingcute:check-line',
'close': 'mingcute:close-line',
'warning': 'mingcute:alert-line',
'error': 'mingcute:close-circle-line',
'info': 'mingcute:information-line',
'success': 'mingcute:check-circle-line',
'refresh': 'mingcute:refresh-2-line',
'search': 'mingcute:search-line',
'filter': 'mingcute:filter-line',
'add': 'mingcute:add-line',
'delete': 'mingcute:delete-2-line',
'edit': 'mingcute:edit-2-line',
'save': 'mingcute:save-line',
'copy': 'mingcute:copy-2-line',
'external': 'mingcute:external-link-line',
'download': 'mingcute:download-2-line',
'upload': 'mingcute:upload-2-line',

// Data & Connections
'database': 'mingcute:server-line',
'connection': 'mingcute:wifi-line',
'disconnected': 'mingcute:wifi-off-line',
'play': 'mingcute:play-fill',
'pause': 'mingcute:pause-fill',
'stop': 'mingcute:stop-fill',

// UI Elements
'chevron-right': 'mingcute:right-line',
'chevron-down': 'mingcute:down-line',
'chevron-up': 'mingcute:up-line',
'chevron-left': 'mingcute:left-line',
'menu': 'mingcute:menu-line',
'more': 'mingcute:more-2-line',
'user': 'mingcute:user-3-line',
'clock': 'mingcute:time-line',
'calendar': 'mingcute:calendar-line',
'sun': 'mingcute:sun-line',
'moon': 'mingcute:moon-line',
'eye': 'mingcute:eye-line',
'eye-off': 'mingcute:eye-close-line',

// Spinners (from svg-spinners)
'spinner': 'svg-spinners:ring-resize',
'spinner-dots': 'svg-spinners:3-dots-bounce',
'spinner-pulse': 'svg-spinners:pulse-3',
'spinner-bars': 'svg-spinners:bars-scale-middle',
'loading': 'svg-spinners:ring-resize',
};

// Get the actual icon name, fallback to passed name if not in map
const iconName = $derived(iconMap[name] || name);
</script>

<IconifyIcon icon={iconName} class={className} width={size} height={size} />
