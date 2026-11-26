import { createInertiaApp } from '@inertiajs/vue3';
import './app.css';
import { initializeTheme } from './composables/useAppearance';
import { signalr } from './lib/inertia-signalr';

const appName = import.meta.env.VITE_APP_NAME || 'Inertia Kitchen Sink';

void createInertiaApp({
    live: signalr(),
    title: (title) => (title ? `${title} - ${appName}` : appName),
    defaults: {
        visitOptions: (href, options) => ({
            preserveScroll: options?.preserveScroll ?? 'errors',
            ...options,
        }),
    },
});

// This will set light / dark mode on page load...
initializeTheme();
