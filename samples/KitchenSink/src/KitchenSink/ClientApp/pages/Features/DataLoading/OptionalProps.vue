<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    regularData: {
        timestamp: string;
        message: string;
    };
    optionalData?: {
        generatedAt: string;
        contacts: Array<{ id: number; name: string }>;
    };
    deferredData?: {
        generatedAt: string;
        totalContacts: number;
    };
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Optional Props' },
];
</script>

<template>
    <Head title="Optional Props" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Optional Props"
                docs="data-props/partial-reloads#lazy-data-evaluation"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L162"
            >
                Compare regular, optional, and deferred prop loading behaviors.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-3">
                <!-- Regular prop -->
                <FeatureCard
                    title="Regular Prop"
                    description="Always included in the response. Available immediately."
                >
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Status</span>
                            <Badge variant="default">Loaded</Badge>
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Timestamp</span>
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ regularData.timestamp }}</Badge
                            >
                        </div>
                        <p class="text-xs text-muted-foreground">
                            {{ regularData.message }}
                        </p>
                    </div>
                </FeatureCard>

                <!-- Optional prop -->
                <FeatureCard title="Optional Prop">
                    <template #description>
                        <code class="text-xs">Inertia::optional()</code>. Only
                        loaded when explicitly requested via partial reload.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Status</span>
                            <Badge
                                :variant="
                                    optionalData ? 'default' : 'secondary'
                                "
                            >
                                {{ optionalData ? 'Loaded' : 'Not loaded' }}
                            </Badge>
                        </div>
                        <template v-if="optionalData">
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >Generated At</span
                                >
                                <Badge
                                    variant="outline"
                                    class="font-mono text-xs"
                                    >{{ optionalData.generatedAt }}</Badge
                                >
                            </div>
                            <div class="space-y-1">
                                <span class="text-sm font-medium"
                                    >Contacts</span
                                >
                                <div
                                    v-for="contact in optionalData.contacts"
                                    :key="contact.id"
                                    class="rounded bg-muted px-2 py-1 text-xs"
                                >
                                    {{ contact.name }}
                                </div>
                            </div>
                        </template>
                        <p v-else class="text-xs text-muted-foreground">
                            Click "Load Optional Data" to fetch this prop.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Deferred prop -->
                <FeatureCard title="Deferred Prop">
                    <template #description>
                        <code class="text-xs">Inertia::defer()</code>.
                        Automatically loaded after the initial page render.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Status</span>
                            <Badge
                                :variant="
                                    deferredData ? 'default' : 'secondary'
                                "
                            >
                                {{ deferredData ? 'Loaded' : 'Loading...' }}
                            </Badge>
                        </div>
                        <template v-if="deferredData">
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >Generated At</span
                                >
                                <Badge
                                    variant="outline"
                                    class="font-mono text-xs"
                                    >{{ deferredData.generatedAt }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >Total Contacts</span
                                >
                                <Badge variant="secondary">{{
                                    deferredData.totalContacts
                                }}</Badge>
                            </div>
                        </template>
                        <p v-else class="text-xs text-muted-foreground">
                            Automatically loads after the initial page render...
                        </p>
                    </div>
                </FeatureCard>
            </div>

            <!-- Controls -->
            <FeatureCard
                title="Controls"
                description="Trigger different reload strategies to see how each prop type behaves."
            >
                <div class="space-y-4">
                    <div class="flex flex-wrap gap-2">
                        <Button
                            @click="router.reload({ only: ['optionalData'] })"
                        >
                            Load Optional Data
                        </Button>
                        <Button
                            variant="outline"
                            @click="router.reload({ only: ['regularData'] })"
                        >
                            Reload Regular Only
                        </Button>
                        <Button variant="outline" @click="router.reload()">
                            Reload All
                        </Button>
                    </div>

                    <div
                        class="rounded-lg border border-black/5 bg-neutral-50/80 p-3 font-mono text-xs dark:border-white/5 dark:bg-neutral-900/80"
                    >
                        <p><strong>Key Differences:</strong></p>
                        <ul class="mt-1 list-inside list-disc space-y-1">
                            <li>
                                <strong>Regular</strong>: always resolved on
                                full visits. Excluded from partial reloads
                                unless explicitly requested.
                            </li>
                            <li>
                                <strong>Optional</strong>: never resolved unless
                                explicitly requested with
                                <code>only: ['optionalData']</code>.
                            </li>
                            <li>
                                <strong>Deferred</strong>: not included on
                                initial load, but automatically fetched
                                client-side after render.
                            </li>
                        </ul>
                    </div>
                </div>
            </FeatureCard>
        </div>
    </AppLayout>
</template>
