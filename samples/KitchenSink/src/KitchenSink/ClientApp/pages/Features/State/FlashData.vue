<script setup lang="ts">
import { Head, router, usePage } from '@inertiajs/vue3';
import { ref, watch, onUnmounted } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'State Management' },
    { title: 'Flash Data' },
];

const page = usePage();
const eventLog = ref<string[]>([]);
const flashListenerActive = ref(false);
let removeFlashListener: (() => void) | null = null;

function log(message: string) {
    eventLog.value.unshift(`${new Date().toLocaleTimeString()} - ${message}`);
    if (eventLog.value.length > 15) eventLog.value.pop();
}

watch(
    () => page.flash,
    (flash) => {
        if (flash.message) {
            log(
                `Flash received: "${flash.message}" (type: ${flash.type || 'default'})`,
            );
        }
    },
    { deep: true },
);

function registerFlashListener() {
    if (removeFlashListener) return;
    removeFlashListener = router.on('flash', (event) => {
        const flash = event.detail.flash;
        log(`[router.on('flash')] keys: ${Object.keys(flash).join(', ')}`);
    });
    flashListenerActive.value = true;
    log('Flash event listener registered');
}

function unregisterFlashListener() {
    if (!removeFlashListener) return;
    removeFlashListener();
    removeFlashListener = null;
    flashListenerActive.value = false;
    log('Flash event listener removed');
}

onUnmounted(() => {
    removeFlashListener?.();
});
</script>

<template>
    <Head title="Flash Data" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Flash Data"
                docs="data-props/flash-data"
                controller="app/Http/Controllers/Feature/StateController.php#L16"
            >
                One-time messages with
                <code class="text-xs">Inertia::flash()</code> (server) and
                <code class="text-xs">router.flash()</code> (client).
            </FeatureHeader>

            <!-- Flash display -->
            <div
                v-if="page.flash.message"
                class="rounded-lg border p-3 text-sm"
                :class="{
                    'border-green-200 bg-green-50 text-green-800 dark:border-green-800 dark:bg-green-950 dark:text-green-200':
                        page.flash.type === 'success',
                    'border-red-200 bg-red-50 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200':
                        page.flash.type === 'error',
                    'border-yellow-200 bg-yellow-50 text-yellow-800 dark:border-yellow-800 dark:bg-yellow-950 dark:text-yellow-200':
                        page.flash.type === 'warning',
                    'border-blue-200 bg-blue-50 text-blue-800 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-200':
                        !page.flash.type,
                }"
            >
                <div class="flex items-center gap-2">
                    <span>{{ page.flash.message }}</span>
                    <Badge variant="outline" class="text-xs">{{
                        page.flash.type || 'default'
                    }}</Badge>
                </div>
            </div>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Server-side flash -->
                <FeatureCard title="Server-Side Flash">
                    <template #description>
                        <code class="text-xs"
                            >Inertia::flash('key', 'value')->back()</code
                        >. Sends flash with a server request.
                    </template>
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                size="sm"
                                @click="
                                    router.post(
                                        '/features/state/flash-data',
                                        {},
                                        { preserveScroll: true },
                                    )
                                "
                            >
                                Success Flash
                            </Button>
                            <Button
                                variant="destructive"
                                size="sm"
                                @click="
                                    router.post(
                                        '/features/state/flash-data/error',
                                        {},
                                        { preserveScroll: true },
                                    )
                                "
                            >
                                Error Flash
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.post(
                                        '/features/state/flash-data/warning',
                                        {},
                                        { preserveScroll: true },
                                    )
                                "
                            >
                                Warning Flash
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Each button sends a POST, the server responds with
                            <code>Inertia::flash()</code> and a redirect back.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Client-side flash -->
                <FeatureCard title="Client-Side Flash">
                    <template #description>
                        <code class="text-xs">router.flash('key', 'value')</code
                        >. Sets flash without a server request.
                    </template>
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.flash({
                                        message: 'Client-side flash!',
                                        type: 'success',
                                    })
                                "
                            >
                                Client Flash (object)
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.flash(
                                        'message',
                                        'Hello from the client!',
                                    )
                                "
                            >
                                Client Flash (key/value)
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            No server request. Flash data is set directly on the
                            client and available via
                            <code>usePage().flash</code>.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Callback form of router.flash() -->
                <FeatureCard title="Callback Flash">
                    <template #description>
                        <code class="text-xs"
                            >router.flash((current) => ({ ...current, key: value
                            }))</code
                        >. Merge with existing flash.
                    </template>
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.flash(
                                        (current: Record<string, unknown>) => ({
                                            ...current,
                                            counter:
                                                ((current.counter as number) ||
                                                    0) + 1,
                                        }),
                                    )
                                "
                            >
                                Increment Counter
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.flash(
                                        (current: Record<string, unknown>) => ({
                                            ...current,
                                            message: 'Merged!',
                                            type: 'success',
                                        }),
                                    )
                                "
                            >
                                Merge Message
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            The callback receives current flash data and returns
                            the new flash state, enabling incremental updates.
                        </p>
                    </div>
                </FeatureCard>

                <!-- router.on('flash') listener -->
                <FeatureCard title="Flash Event Listener">
                    <template #description>
                        <code class="text-xs">router.on('flash', callback)</code
                        >. Fires whenever flash data is received.
                    </template>
                    <div class="space-y-4">
                        <div class="flex items-center gap-2">
                            <Badge
                                :variant="
                                    flashListenerActive
                                        ? 'default'
                                        : 'secondary'
                                "
                                class="text-xs"
                                >{{
                                    flashListenerActive ? 'Active' : 'Inactive'
                                }}</Badge
                            >
                            <Button
                                v-if="!flashListenerActive"
                                size="sm"
                                @click="registerFlashListener"
                                >Register</Button
                            >
                            <Button
                                v-else
                                variant="outline"
                                size="sm"
                                @click="unregisterFlashListener"
                                >Remove</Button
                            >
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Register the listener, then trigger any flash. The
                            event log will show entries from both the
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >watch</code
                            >
                            and the
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >router.on('flash')</code
                            >
                            listener.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Flash state display -->
                <FeatureCard info-card title="Current Flash State">
                    <template #description>
                        <code class="text-xs">usePage().flash</code>. Reactive
                        access to current flash data.
                    </template>
                    <CodeBlock :code="JSON.stringify(page.flash, null, 2)" />
                    <p class="mt-2 text-xs text-muted-foreground">
                        Flash data is one-time. It clears after being sent to
                        the client and is not persisted in browser history
                        state.
                    </p>
                </FeatureCard>

                <!-- Event Log -->
                <FeatureCard info-card title="Event Log">
                    <template #header-action>
                        <Button variant="ghost" size="sm" @click="eventLog = []"
                            >Clear</Button
                        >
                    </template>
                    <div v-if="eventLog.length" class="space-y-1">
                        <div
                            v-for="(entry, i) in eventLog"
                            :key="i"
                            class="rounded bg-muted px-2 py-1 font-mono text-xs"
                        >
                            {{ entry }}
                        </div>
                    </div>
                    <p v-else class="text-xs text-muted-foreground">
                        Trigger a flash to see events.
                    </p>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
