<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { onUnmounted, ref } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Events & Lifecycle' },
    { title: 'Inertia.once' },
];

const eventLog = ref<string[]>([]);
const pendingRemovers: (() => void)[] = [];

function log(message: string) {
    eventLog.value.unshift(`${new Date().toLocaleTimeString()} - ${message}`);
    if (eventLog.value.length > 20) eventLog.value.pop();
}

function registerOnceBefore() {
    log('Registered router.once("before") confirm listener');
    router.once('before', (event) => {
        const url = event.detail.visit.url.toString();
        log(`once(before) fired for ${url}`);
        return confirm(`Allow visit to ${url}?`);
    });
}

function registerOnceSuccess() {
    log('Registered router.once("success") listener');
    router.once('success', () => {
        log('once(success) fired');
    });
}

function compareWithOn() {
    log('Registered router.on("before") confirm listener');
    const remove = router.on('before', (event) => {
        const url = event.detail.visit.url.toString();
        log(`on(before) fired for ${url}`);
        return confirm(`Allow visit to ${url}?`);
    });
    pendingRemovers.push(remove);
}

function clearOnListeners() {
    pendingRemovers.forEach((r) => r());
    pendingRemovers.length = 0;
    log('Cleared all router.on listeners');
}

function fireAction() {
    router.post(
        '/features/events/once-events/action',
        {},
        { preserveScroll: true },
    );
}

onUnmounted(() => {
    pendingRemovers.forEach((r) => r());
});
</script>

<template>
    <Head title="Inertia.once" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Inertia.once"
                docs="advanced/events#registering-listeners"
                controller="app/Http/Controllers/Feature/EventController.php#L22"
            >
                Listen to a router event exactly once with
                <code class="text-xs">router.once()</code>. Listener
                auto-removes after the first invocation.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard
                    title="Register once listeners"
                    badge="v3.2"
                    description="Register a listener, then trigger a visit. once(before) returns confirm() to gate the visit, then auto-removes."
                >
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                size="sm"
                                data-test="register-once-before"
                                @click="registerOnceBefore"
                            >
                                once('before')
                            </Button>
                            <Button
                                size="sm"
                                data-test="register-once-success"
                                @click="registerOnceSuccess"
                            >
                                once('success')
                            </Button>
                        </div>

                        <div class="flex flex-wrap gap-2">
                            <Button
                                size="sm"
                                variant="secondary"
                                @click="router.reload()"
                            >
                                Reload page
                            </Button>
                            <Button
                                size="sm"
                                variant="secondary"
                                @click="fireAction"
                            >
                                POST action
                            </Button>
                        </div>

                        <CodeBlock
                            title="Inertia.once"
                            code="
                                router.once('before', (event) => {
                                  return confirm('Allow visit to ' + event.detail.visit.url + '?')
                                })

                                // Equivalent to the self-removing pattern:
                                const remove = router.on('before', (event) => {
                                  remove()
                                  return confirm('...')
                                })
                            "
                        />
                    </div>
                </FeatureCard>

                <FeatureCard
                    title="Compare with router.on"
                    description="A plain router.on listener keeps firing until removed. Register it, then trigger multiple visits to contrast with once()."
                >
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" @click="compareWithOn">
                                Register on('before')
                            </Button>
                            <Button
                                size="sm"
                                variant="destructive"
                                @click="clearOnListeners"
                            >
                                Clear on listeners
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Fire several reloads after registering.
                            <code>on(before)</code> prompts every visit until
                            cleared. <code>once(before)</code> prompts the next
                            visit, then auto-removes.
                        </p>
                    </div>
                </FeatureCard>

                <FeatureCard info-card class="lg:col-span-2" title="Event Log">
                    <template #header-action>
                        <Button
                            variant="ghost"
                            size="sm"
                            @click="eventLog = []"
                        >
                            Clear
                        </Button>
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
                    <Badge v-else variant="outline">No events yet</Badge>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
