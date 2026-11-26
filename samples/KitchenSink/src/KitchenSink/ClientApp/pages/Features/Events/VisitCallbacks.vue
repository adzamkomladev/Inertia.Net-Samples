<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Events & Lifecycle' },
    { title: 'Visit Callbacks' },
];

const eventLog = ref<string[]>([]);

function log(event: string, detail?: string) {
    eventLog.value.unshift(
        `${new Date().toLocaleTimeString()} - ${event}${detail ? ': ' + detail : ''}`,
    );
    if (eventLog.value.length > 20) eventLog.value.pop();
}

function triggerWithCallbacks() {
    router.post(
        '/features/events/visit-callbacks/action',
        {},
        {
            preserveScroll: true,
            onBefore: () => {
                log('onBefore', 'visit is about to start');
            },
            onStart: () => {
                log('onStart', 'request started');
            },
            onProgress: (progress) => {
                log('onProgress', `${progress?.percentage}%`);
            },
            onSuccess: () => {
                log('onSuccess', 'response received');
            },
            onError: (errors) => {
                log('onError', JSON.stringify(errors));
            },
            onFinish: () => {
                log('onFinish', 'request completed');
            },
        },
    );
}

function triggerWithCancel() {
    router.post(
        '/features/events/visit-callbacks/action',
        {},
        {
            preserveScroll: true,
            onBefore: () => {
                log('onBefore', 'returning false, visit cancelled!');
                return false;
            },
            onCancel: () => {
                log('onCancel', 'visit was cancelled');
            },
        },
    );
}

function triggerWithCancelToken() {
    let token: { cancel: () => void } | null = null;

    router.post(
        '/features/events/visit-callbacks/action',
        {},
        {
            preserveScroll: true,
            onCancelToken: (cancelToken) => {
                token = cancelToken;
                log('onCancelToken', 'received cancel token');
                // Cancel after 50ms to demonstrate
                setTimeout(() => {
                    token?.cancel();
                    log('manual cancel', 'called cancelToken.cancel()');
                }, 50);
            },
            onCancel: () => {
                log('onCancel', 'visit was cancelled via token');
            },
            onFinish: () => {
                log('onFinish', 'finished after cancellation');
            },
        },
    );
}
</script>

<template>
    <Head title="Visit Callbacks" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Visit Callbacks"
                docs="advanced/events#event-callbacks"
                controller="app/Http/Controllers/Feature/EventController.php#L22"
            >
                Per-visit event hooks passed as options to
                <code class="text-xs">router.visit()</code>,
                <code class="text-xs">router.post()</code>, etc.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard
                    title="Trigger Callbacks"
                    description="Each button demonstrates different visit callback behaviors."
                >
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" @click="triggerWithCallbacks">
                                Full Lifecycle
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="triggerWithCancel"
                            >
                                Cancel via onBefore
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="triggerWithCancelToken"
                            >
                                Cancel via Token
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Watch the event log to see the callback sequence.
                        </p>
                    </div>
                </FeatureCard>

                <FeatureCard
                    info-card
                    title="Available Callbacks"
                    description="All per-visit event hooks."
                >
                    <div class="space-y-2 text-xs">
                        <div class="flex justify-between">
                            <code>onBefore</code>
                            <span class="text-muted-foreground"
                                >Return false to cancel</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onCancelToken</code>
                            <span class="text-muted-foreground"
                                >Receive cancel token</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onStart</code>
                            <span class="text-muted-foreground"
                                >Request started</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onProgress</code>
                            <span class="text-muted-foreground"
                                >Upload progress</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onSuccess</code>
                            <span class="text-muted-foreground"
                                >2xx response</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onError</code>
                            <span class="text-muted-foreground"
                                >422 validation errors</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onHttpException</code>
                            <span class="text-muted-foreground"
                                >Non-Inertia responses</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onNetworkError</code>
                            <span class="text-muted-foreground"
                                >Network failures</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onCancel</code>
                            <span class="text-muted-foreground"
                                >Visit was cancelled</span
                            >
                        </div>
                        <div class="flex justify-between">
                            <code>onFinish</code>
                            <span class="text-muted-foreground"
                                >Always runs last</span
                            >
                        </div>
                    </div>
                </FeatureCard>

                <FeatureCard info-card class="lg:col-span-2" title="Event Log">
                    <template #header-action>
                        <Button variant="ghost" size="sm" @click="eventLog = []"
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
                        >
                            {{ entry }}
                        </div>
                    </div>
                    <p v-else class="text-xs text-muted-foreground">
                        Click a button to see visit callbacks.
                    </p>
                </FeatureCard>

                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Code Example"
                >
                    <CodeBlock
                        code="
                        router.post('/endpoint', data, {
                          onBefore: (visit) => confirm('Proceed?'),
                          onStart: (visit) => console.log('Started'),
                          onSuccess: (page) => console.log('Success!'),
                          onError: (errors) => console.log(errors),
                          onFinish: (visit) => console.log('Done'),
                        })
                    "
                    />
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
