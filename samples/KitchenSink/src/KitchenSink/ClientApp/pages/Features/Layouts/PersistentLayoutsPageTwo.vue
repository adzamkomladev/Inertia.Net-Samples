<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import { ref } from 'vue';
import FeatureCard from '@/components/FeatureCard.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import PersistentDemoLayout from '@/layouts/PersistentDemoLayout.vue';

defineOptions({ layout: PersistentDemoLayout });

const name = ref('');
const email = ref('');
</script>

<template>
    <Head title="Persistent Layouts - Page 2" />

    <div class="grid gap-6 lg:grid-cols-2">
        <FeatureCard
            title="Page 2 State"
            description="A different page component with its own state. This also resets on navigation."
        >
            <div class="space-y-4">
                <div class="space-y-2">
                    <Label for="name">Name</Label>
                    <Input
                        id="name"
                        v-model="name"
                        placeholder="Type something..."
                    />
                </div>
                <div class="space-y-2">
                    <Label for="email">Email</Label>
                    <Input
                        id="email"
                        v-model="email"
                        placeholder="Type something..."
                    />
                </div>

                <p class="text-xs text-muted-foreground">
                    Type in these fields, switch to Page 1, then come back. The
                    inputs clear because this page component remounts. The
                    layout state above stays intact.
                </p>
            </div>
        </FeatureCard>

        <FeatureCard
            info-card
            title="What Persists"
            description="Everything in the layout component survives navigation between pages that share it."
        >
            <div class="space-y-3">
                <div class="space-y-2 text-xs text-muted-foreground">
                    <p>
                        <span class="font-medium text-foreground"
                            >Refs and reactive state</span
                        >
                        keep their values (stopwatch, counter).
                    </p>
                    <p>
                        <span class="font-medium text-foreground"
                            >Intervals and timers</span
                        >
                        continue running without interruption.
                    </p>
                    <p>
                        <span class="font-medium text-foreground"
                            >Event listeners</span
                        >
                        registered in the layout stay attached.
                    </p>
                    <p>
                        <span class="font-medium text-foreground"
                            >onMounted</span
                        >
                        only fires once, not on every navigation.
                    </p>
                </div>
                <p class="text-xs text-muted-foreground">
                    Click "Leave layout" to navigate away. When you return, the
                    layout remounts and all state resets.
                </p>
            </div>
        </FeatureCard>
    </div>
</template>
