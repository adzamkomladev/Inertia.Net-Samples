import { router } from '@inertiajs/vue3';
import { onMounted, onUnmounted, ref } from 'vue';

export type Toast = {
    id: number;
    message: string;
    type: 'success' | 'error' | 'warning';
};

export const toasts = ref<Toast[]>([]);

let nextId = 0;

export function dismissToast(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id);
}

export function addToast(message: string, type: Toast['type'] = 'success') {
    const id = nextId++;
    toasts.value.push({ id, message, type });
    setTimeout(() => dismissToast(id), 4000);
}

export function useFlashToast() {
    let removeListener: (() => void) | null = null;

    onMounted(() => {
        removeListener = router.on('flash', (event) => {
            const flash = event.detail.flash as Record<string, unknown>;
            const message = flash.message as string | undefined;

            if (!message) {
                return;
            }

            const type = (flash.type as Toast['type']) || 'success';
            addToast(message, type);
        });
    });

    onUnmounted(() => {
        removeListener?.();
    });
}
