<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    timestamp: string;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Async Requests' },
];

const eventLog = ref<string[]>([]);
let requestCounter = 0;

function log(message: string) {
    eventLog.value.unshift(`${new Date().toLocaleTimeString()} - ${message}`);
    if (eventLog.value.length > 20) {
        eventLog.value.pop();
    }
}

function sendSyncRequest() {
    const id = ++requestCounter;
    log(`[#${id}] Sending SYNC request...`);

    router.visit('/features/navigation/async-requests', {
        preserveScroll: true,
        preserveState: true,
        onStart: () => log(`[#${id}] Started`),
        onSuccess: () => log(`[#${id}] Completed`),
        onCancel: () => log(`[#${id}] Cancelled (replaced by newer request)`),
    });
}

function sendAsyncRequest() {
    const id = ++requestCounter;
    log(`[#${id}] Sending ASYNC request...`);

    router.visit('/features/navigation/async-requests', {
        async: true,
        preserveScroll: true,
        preserveState: true,
        onStart: () => log(`[#${id}] Started (async)`),
        onSuccess: () => log(`[#${id}] Completed (async)`),
    });
}

let cancelToken: { cancel: () => void } | null = null;

function sendCancellable() {
    const id = ++requestCounter;
    log(`[#${id}] Sending cancellable request (2s delay)...`);

    router.get(
        '/features/navigation/async-slow',
        { delay: 2 },
        {
            preserveScroll: true,
            preserveState: true,
            onCancelToken: (token) => {
                cancelToken = token;
            },
            onStart: () => log(`[#${id}] Started (cancellable)`),
            onSuccess: () => {
                log(`[#${id}] Completed`);
                cancelToken = null;
            },
            onCancel: () => {
                log(`[#${id}] Cancelled by user`);
                cancelToken = null;
            },
        },
    );
}

function cancelRequest() {
    if (cancelToken) {
        cancelToken.cancel();
    } else {
        log('No active cancellable request');
    }
}
</script>

<template>
    <Head title="Async Requests" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Async Requests"
                docs="the-basics/manual-visits"
                controller="app/Http/Controllers/Feature/NavigationController.php#L58"
            >
                Request queuing behavior. Sync (default) vs async concurrent
                requests.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Sync Requests -->
                <FeatureCard
                    title="Synchronous (Default)"
                    description="Each new request cancels any in-flight request. Click rapidly to see cancellation."
                >
                    <div class="space-y-3">
                        <Button @click="sendSyncRequest()">
                            Send Sync Request
                        </Button>
                        <p class="text-xs text-muted-foreground">
                            Click multiple times quickly. Previous requests get
                            cancelled.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Async Requests -->
                <FeatureCard title="Async (Concurrent)">
                    <template #description>
                        With
                        <code class="rounded bg-muted px-1 py-0.5 text-xs"
                            >async: true</code
                        >, requests run concurrently without cancelling each
                        other.
                    </template>
                    <div class="space-y-3">
                        <Button @click="sendAsyncRequest()">
                            Send Async Request
                        </Button>
                        <p class="text-xs text-muted-foreground">
                            Click multiple times. All requests complete
                            independently.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Cancel Token -->
                <FeatureCard title="Cancel Token">
                    <template #description>
                        Use
                        <code class="rounded bg-muted px-1 py-0.5 text-xs"
                            >onCancelToken</code
                        >
                        to manually cancel a specific request.
                    </template>
                    <div class="space-y-3">
                        <div class="flex gap-2">
                            <Button @click="sendCancellable()">
                                Send Slow Request (2s)
                            </Button>
                            <Button
                                variant="destructive"
                                @click="cancelRequest()"
                            >
                                Cancel
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Start a slow request, then cancel it before it
                            completes.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Server State -->
                <FeatureCard info-card title="Server Response">
                    <div class="flex items-center gap-2">
                        <span class="text-sm text-muted-foreground"
                            >Timestamp:</span
                        >
                        <Badge variant="outline">{{ timestamp }}</Badge>
                    </div>
                </FeatureCard>

                <!-- Event Log -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Request Log"
                >
                    <template #header-action>
                        <Button
                            variant="ghost"
                            size="sm"
                            @click="
                                eventLog = [];
                                requestCounter = 0;
                            "
                            >Clear</Button
                        >
                    </template>
                    <div
                        v-if="eventLog.length"
                        class="max-h-64 space-y-1 overflow-y-auto"
                    >
                        <div
                            v-for="(entry, i) in eventLog"
                            :key="i"
                            class="rounded bg-muted px-2 py-1 font-mono text-xs"
                            :class="{
                                'text-red-600 dark:text-red-400':
                                    entry.includes('Cancelled'),
                                'text-green-600 dark:text-green-400':
                                    entry.includes('Completed'),
                            }"
                        >
                            {{ entry }}
                        </div>
                    </div>
                    <p v-else class="text-xs text-muted-foreground">
                        Click buttons above to see request flow.
                    </p>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
