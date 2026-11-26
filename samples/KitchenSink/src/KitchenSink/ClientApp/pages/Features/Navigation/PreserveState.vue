<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    serverCounter: number;
    timestamp: string;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Preserve State' },
];

const localCounter = ref(0);
const textInput = ref('');
</script>

<template>
    <Head title="Preserve State" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Preserve State"
                docs="the-basics/manual-visits#state-preservation"
                controller="app/Http/Controllers/Feature/NavigationController.php#L25"
            >
                Compare navigation with and without preserveState to see how
                local component state behaves.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Local State -->
                <FeatureCard
                    title="Local Component State"
                    description="These values live in the Vue component. They reset on full re-render."
                >
                    <div class="space-y-4">
                        <div class="flex items-center gap-3">
                            <span class="text-sm font-medium">Counter:</span>
                            <Badge
                                variant="outline"
                                class="min-w-10 justify-center text-lg tabular-nums"
                                >{{ localCounter }}</Badge
                            >
                            <Button size="sm" @click="localCounter++"
                                >+1</Button
                            >
                            <Button
                                size="sm"
                                variant="outline"
                                @click="localCounter = 0"
                                >Reset</Button
                            >
                        </div>

                        <div class="space-y-2">
                            <span class="text-sm font-medium">Text input:</span>
                            <Input
                                v-model="textInput"
                                placeholder="Type something here..."
                            />
                        </div>
                    </div>
                </FeatureCard>

                <!-- Server State -->
                <FeatureCard
                    info-card
                    title="Server Props"
                    description="These come from the server and always update."
                >
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >serverCounter</span
                            >
                            <Badge variant="secondary">{{
                                serverCounter
                            }}</Badge>
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">timestamp</span>
                            <Badge variant="secondary">{{ timestamp }}</Badge>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Navigation Options -->
                <FeatureCard info-card class="lg:col-span-2" title="Try It">
                    <template #description>
                        Increment the counter and type some text, then click
                        each button below. Notice how
                        <code class="rounded bg-muted px-1 py-0.5 text-xs"
                            >preserveState</code
                        >
                        keeps local state while still updating server props.
                    </template>
                    <div class="flex flex-wrap gap-3">
                        <div class="space-y-1">
                            <Button
                                variant="destructive"
                                @click="
                                    router.visit(
                                        '/features/navigation/preserve-state',
                                    )
                                "
                            >
                                Without preserveState
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Counter & input reset to defaults.
                            </p>
                        </div>
                        <div class="space-y-1">
                            <Button
                                @click="
                                    router.visit(
                                        '/features/navigation/preserve-state',
                                        { preserveState: true },
                                    )
                                "
                            >
                                With preserveState: true
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Counter & input are preserved.
                            </p>
                        </div>
                        <div class="space-y-1">
                            <Button
                                variant="secondary"
                                @click="router.reload()"
                            >
                                router.reload()
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Reload preserves state by default.
                            </p>
                        </div>
                    </div>

                    <div class="mt-4 flex flex-wrap gap-3">
                        <div class="space-y-1">
                            <Link
                                href="/features/navigation/preserve-state"
                                class="inline-flex items-center rounded-md border border-black/10 bg-background px-3 py-1.5 text-sm hover:bg-accent dark:border-white/10"
                            >
                                &lt;Link&gt; (no preserveState)
                            </Link>
                            <p class="text-xs text-muted-foreground">
                                State resets.
                            </p>
                        </div>
                        <div class="space-y-1">
                            <Link
                                href="/features/navigation/preserve-state"
                                preserve-state
                                class="inline-flex items-center rounded-md border border-black/10 bg-background px-3 py-1.5 text-sm hover:bg-accent dark:border-white/10"
                            >
                                &lt;Link preserve-state&gt;
                            </Link>
                            <p class="text-xs text-muted-foreground">
                                State preserved.
                            </p>
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
