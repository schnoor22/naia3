// vite.config.ts
import { sveltekit } from "file:///C:/naia3/src/Naia.Web/node_modules/@sveltejs/kit/src/exports/vite/index.js";
import { defineConfig } from "file:///C:/naia3/src/Naia.Web/node_modules/vite/dist/node/index.js";
var vite_config_default = defineConfig({
  plugins: [sveltekit()],
  build: {
    sourcemap: true
    // Enable source maps for debugging
  },
  server: {
    port: 5173,
    proxy: {
      // Proxy API calls to .NET backend during development
      "/api": {
        target: "http://localhost:5052",
        changeOrigin: true
      },
      "/health": {
        target: "http://localhost:5052",
        changeOrigin: true
      },
      "/hangfire": {
        target: "http://localhost:5052",
        changeOrigin: true
      },
      // SignalR WebSocket proxy
      "/hubs": {
        target: "http://localhost:5052",
        changeOrigin: true,
        ws: true
      }
    }
  }
});
export {
  vite_config_default as default
};
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsidml0ZS5jb25maWcudHMiXSwKICAic291cmNlc0NvbnRlbnQiOiBbImNvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9kaXJuYW1lID0gXCJDOlxcXFxuYWlhM1xcXFxzcmNcXFxcTmFpYS5XZWJcIjtjb25zdCBfX3ZpdGVfaW5qZWN0ZWRfb3JpZ2luYWxfZmlsZW5hbWUgPSBcIkM6XFxcXG5haWEzXFxcXHNyY1xcXFxOYWlhLldlYlxcXFx2aXRlLmNvbmZpZy50c1wiO2NvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9pbXBvcnRfbWV0YV91cmwgPSBcImZpbGU6Ly8vQzovbmFpYTMvc3JjL05haWEuV2ViL3ZpdGUuY29uZmlnLnRzXCI7aW1wb3J0IHsgc3ZlbHRla2l0IH0gZnJvbSAnQHN2ZWx0ZWpzL2tpdC92aXRlJztcclxuaW1wb3J0IHsgZGVmaW5lQ29uZmlnIH0gZnJvbSAndml0ZSc7XHJcblxyXG5leHBvcnQgZGVmYXVsdCBkZWZpbmVDb25maWcoe1xyXG5cdHBsdWdpbnM6IFtzdmVsdGVraXQoKV0sXHJcblx0YnVpbGQ6IHtcclxuXHRcdHNvdXJjZW1hcDogdHJ1ZSwgLy8gRW5hYmxlIHNvdXJjZSBtYXBzIGZvciBkZWJ1Z2dpbmdcclxuXHR9LFxyXG5cdHNlcnZlcjoge1xyXG5cdFx0cG9ydDogNTE3MyxcclxuXHRcdHByb3h5OiB7XHJcblx0XHRcdC8vIFByb3h5IEFQSSBjYWxscyB0byAuTkVUIGJhY2tlbmQgZHVyaW5nIGRldmVsb3BtZW50XHJcblx0XHRcdCcvYXBpJzoge1xyXG5cdFx0XHRcdHRhcmdldDogJ2h0dHA6Ly9sb2NhbGhvc3Q6NTA1MicsXHJcblx0XHRcdFx0Y2hhbmdlT3JpZ2luOiB0cnVlXHJcblx0XHRcdH0sXHJcblx0XHRcdCcvaGVhbHRoJzoge1xyXG5cdFx0XHRcdHRhcmdldDogJ2h0dHA6Ly9sb2NhbGhvc3Q6NTA1MicsXHJcblx0XHRcdFx0Y2hhbmdlT3JpZ2luOiB0cnVlXHJcblx0XHRcdH0sXHJcblx0XHRcdCcvaGFuZ2ZpcmUnOiB7XHJcblx0XHRcdFx0dGFyZ2V0OiAnaHR0cDovL2xvY2FsaG9zdDo1MDUyJyxcclxuXHRcdFx0XHRjaGFuZ2VPcmlnaW46IHRydWVcclxuXHRcdFx0fSxcclxuXHRcdFx0Ly8gU2lnbmFsUiBXZWJTb2NrZXQgcHJveHlcclxuXHRcdFx0Jy9odWJzJzoge1xyXG5cdFx0XHRcdHRhcmdldDogJ2h0dHA6Ly9sb2NhbGhvc3Q6NTA1MicsXHJcblx0XHRcdFx0Y2hhbmdlT3JpZ2luOiB0cnVlLFxyXG5cdFx0XHRcdHdzOiB0cnVlXHJcblx0XHRcdH1cclxuXHRcdH1cclxuXHR9XHJcbn0pO1xyXG4iXSwKICAibWFwcGluZ3MiOiAiO0FBQXlQLFNBQVMsaUJBQWlCO0FBQ25SLFNBQVMsb0JBQW9CO0FBRTdCLElBQU8sc0JBQVEsYUFBYTtBQUFBLEVBQzNCLFNBQVMsQ0FBQyxVQUFVLENBQUM7QUFBQSxFQUNyQixPQUFPO0FBQUEsSUFDTixXQUFXO0FBQUE7QUFBQSxFQUNaO0FBQUEsRUFDQSxRQUFRO0FBQUEsSUFDUCxNQUFNO0FBQUEsSUFDTixPQUFPO0FBQUE7QUFBQSxNQUVOLFFBQVE7QUFBQSxRQUNQLFFBQVE7QUFBQSxRQUNSLGNBQWM7QUFBQSxNQUNmO0FBQUEsTUFDQSxXQUFXO0FBQUEsUUFDVixRQUFRO0FBQUEsUUFDUixjQUFjO0FBQUEsTUFDZjtBQUFBLE1BQ0EsYUFBYTtBQUFBLFFBQ1osUUFBUTtBQUFBLFFBQ1IsY0FBYztBQUFBLE1BQ2Y7QUFBQTtBQUFBLE1BRUEsU0FBUztBQUFBLFFBQ1IsUUFBUTtBQUFBLFFBQ1IsY0FBYztBQUFBLFFBQ2QsSUFBSTtBQUFBLE1BQ0w7QUFBQSxJQUNEO0FBQUEsRUFDRDtBQUNELENBQUM7IiwKICAibmFtZXMiOiBbXQp9Cg==
